using System.Text.Json.Serialization;
using OrderGenerator.Application;
using OrderGenerator.Domain;
using OrderGenerator.Infrastructure.Fix;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFixInitiator(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (FixSessionMonitor monitor) => Results.Ok(new
{
    fixLoggedOn = monitor.IsLoggedOn,
    sessions = monitor.LoggedOnSessions
}));

app.MapGet("/api/status", (FixSessionMonitor monitor) => Results.Ok(new
{
    accumulatorOnline = monitor.IsLoggedOn,
    sessions = monitor.Sessions
}));

app.MapPost("/api/orders", async (OrderForm form, IOrderSubmissionService service, CancellationToken ct) =>
{
    var errors = OrderRequestValidator.Validate(form.Quantity, form.Price);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
    }

    var outcome = await service.SubmitAsync(new OrderRequest(form.Symbol, form.Side, form.Quantity, form.Price), ct);
    return Results.Ok(outcome);
});

app.Run();

internal sealed record OrderForm(Symbol Symbol, OrderSide Side, int Quantity, decimal Price);
