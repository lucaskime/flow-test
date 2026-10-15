using System.Text.Json;
using System.Threading.Channels;
using OrderAccumulator.Application;
using OrderAccumulator.Infrastructure.Fix;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFixAcceptor(builder.Configuration);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

object BuildState(IOrderAcceptanceService acceptance, OrderJournal journal, FixSessionMonitor monitor)
{
    var exposures = acceptance.GetExposures();
    var (recent, totals, audit) = journal.Snapshot(100);

    return new
    {
        serverTime = DateTimeOffset.UtcNow,
        limit = OrderAcceptanceService.ExposureLimit,
        fix = new { loggedOn = monitor.IsLoggedOn, sessions = monitor.Sessions },
        totals,
        symbols = exposures.Select(kv =>
        {
            var name = kv.Key.ToString();
            decimal? audited = audit is null ? null : audit.GetValueOrDefault(name);
            return new
            {
                symbol = name,
                exposure = kv.Value,
                withinLimit = Math.Abs(kv.Value) <= OrderAcceptanceService.ExposureLimit,
                audited,
                consistent = audited is null || audited == kv.Value
            };
        }),
        orders = recent
    };
}

app.MapGet("/health", (FixSessionMonitor monitor) => Results.Ok(new
{
    fixLoggedOn = monitor.IsLoggedOn,
    sessions = monitor.LoggedOnSessions
}));

app.MapGet("/api/state", (IOrderAcceptanceService acceptance, OrderJournal journal, FixSessionMonitor monitor) =>
    Results.Json(BuildState(acceptance, journal, monitor), json));

// Server-Sent Events: envia o estado completo a cada mudança (e a cada 15 s como keep-alive).
app.MapGet("/api/stream", async (HttpContext ctx, IOrderAcceptanceService acceptance,
    OrderJournal journal, FixSessionMonitor monitor) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    var signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    void OnChange() => signal.Writer.TryWrite(true);

    journal.Changed += OnChange;
    monitor.Changed += OnChange;
    var ct = ctx.RequestAborted;
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
    var keepAlive = Task.Run(async () =>
    {
        try { while (await timer.WaitForNextTickAsync(ct)) OnChange(); }
        catch (OperationCanceledException) { }
    }, ct);

    try
    {
        OnChange();
        while (await signal.Reader.WaitToReadAsync(ct))
        {
            signal.Reader.TryRead(out _);
            var payload = JsonSerializer.Serialize(BuildState(acceptance, journal, monitor), json);
            await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        journal.Changed -= OnChange;
        monitor.Changed -= OnChange;
    }
});

app.Run();
