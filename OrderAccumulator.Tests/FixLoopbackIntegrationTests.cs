using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using OrderAccumulator.Application;
using OrderAccumulator.Domain;
using OrderAccumulator.Infrastructure.Fix;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;
using Message = QuickFix.Message;
using Symbol = OrderAccumulator.Domain.Symbol;

namespace OrderAccumulator.Tests;

/// <summary>Acceptor e initiator reais do QuickFIX/n em loopback: valida a ponta a ponta pelo protocolo.</summary>
public sealed class FixLoopbackIntegrationTests : IAsyncLifetime
{
    private readonly OrderAcceptanceService _service = new();
    private readonly OrderJournal _journal = new();
    private readonly Client _client = new();
    private ThreadedSocketAcceptor? _acceptor;
    private SocketInitiator? _initiator;

    public async Task InitializeAsync()
    {
        var dictionary = FindDictionary();
        var port = FreePort();

        string Cfg(string type, string sender, string target, string extra) => $"""
            [DEFAULT]
            ConnectionType={type}
            StartTime=00:00:00
            EndTime=00:00:00
            HeartBtInt=30
            UseDataDictionary=Y
            DataDictionary={dictionary}
            ReconnectInterval=1
            [SESSION]
            BeginString=FIX.4.4
            SenderCompID={sender}
            TargetCompID={target}
            {extra}
            """;

        var monitor = new FixSessionMonitor();
        var app = new AcceptorApplication(monitor, _service, _journal, NullLogger<AcceptorApplication>.Instance);
        var acceptorSettings = new SessionSettings(new StringReader(
            Cfg("acceptor", "ACCUMULATOR", "GENERATOR", $"SocketAcceptPort={port}")));
        _acceptor = new ThreadedSocketAcceptor(app, new MemoryStoreFactory(), acceptorSettings, new NullLogFactory());
        _acceptor.Start();

        var initiatorSettings = new SessionSettings(new StringReader(
            Cfg("initiator", "GENERATOR", "ACCUMULATOR", $"SocketConnectHost=127.0.0.1\nSocketConnectPort={port}")));
        _initiator = new SocketInitiator(_client, new MemoryStoreFactory(), initiatorSettings, new NullLogFactory());
        _initiator.Start();

        Assert.True(await _client.LoggedOn.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    public Task DisposeAsync()
    {
        _initiator?.Stop();
        _acceptor?.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Order_within_limit_returns_New_and_over_limit_returns_Rejected()
    {
        var accepted = await _client.SendAsync(NewOrder("L1", "PETR4", Side.BUY, 99_999, 999.99m));
        Assert.Equal(ExecType.NEW, Assert.IsType<ExecutionReport>(accepted).ExecType.Value);

        var rejected = await _client.SendAsync(NewOrder("L2", "PETR4", Side.BUY, 1_000, 10m));
        var report = Assert.IsType<ExecutionReport>(rejected);
        Assert.Equal(ExecType.REJECTED, report.ExecType.Value);
        Assert.Equal(OrdRejReason.ORDER_EXCEEDS_LIMIT, report.OrdRejReason.Value);

        Assert.Equal(99_998_000.01m, _service.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public async Task Resent_ClOrdId_is_counted_once_over_the_wire()
    {
        await _client.SendAsync(NewOrder("R1", "VALE3", Side.BUY, 10, 5m));
        var again = await _client.SendAsync(NewOrder("R1", "VALE3", Side.BUY, 10, 5m));

        Assert.Equal(ExecType.NEW, Assert.IsType<ExecutionReport>(again).ExecType.Value);
        Assert.Equal(50m, _service.GetExposure(Symbol.VALE3));
    }

    [Fact]
    public async Task Concurrent_orders_over_the_wire_never_exceed_limit()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 150).Select(i =>
            _client.SendAsync(NewOrder($"C{i}", "VIIA4", Side.BUY, 1_000, 999.99m))));

        var accepted = responses.OfType<ExecutionReport>().Count(r => r.ExecType.Value == ExecType.NEW);
        Assert.Equal(100, accepted);
        Assert.Equal(150, responses.Length);
        Assert.True(_service.GetExposure(Symbol.VIIA4) <= OrderAcceptanceService.ExposureLimit);
    }

    private static NewOrderSingle NewOrder(string id, string symbol, char side, decimal qty, decimal price)
    {
        var m = new NewOrderSingle(new ClOrdID(id), new QuickFix.Fields.Symbol(symbol), new Side(side),
            new TransactTime(DateTime.UtcNow), new OrdType(OrdType.LIMIT));
        m.Set(new OrderQty(qty));
        m.Set(new Price(price));
        return m;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindDictionary()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "OrderAccumulator", "Configuration", "fix", "FIX44.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("FIX44.xml not found");
    }

    private sealed class Client : MessageCracker, IApplication
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _waiting = new();
        private SessionID? _session;

        public TaskCompletionSource<bool> LoggedOn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Message> SendAsync(NewOrderSingle order)
        {
            var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Reenvios do mesmo ClOrdID aguardam respostas em sequência.
            var key = order.ClOrdID.Value;
            while (!_waiting.TryAdd(key, tcs))
            {
                await Task.Delay(5);
            }

            Assert.True(Session.SendToTarget(order, _session));
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        public void OnCreate(SessionID sessionID) => _session = sessionID;
        public void OnLogon(SessionID sessionID) => LoggedOn.TrySetResult(true);
        public void OnLogout(SessionID sessionID) { }
        public void ToAdmin(Message message, SessionID sessionID) { }
        public void FromAdmin(Message message, SessionID sessionID) { }
        public void ToApp(Message message, SessionID sessionID) { }

        public void FromApp(Message message, SessionID sessionID)
        {
            var id = message.IsSetField(Tags.ClOrdID) ? message.GetString(Tags.ClOrdID)
                : message.IsSetField(Tags.BusinessRejectRefID) ? message.GetString(Tags.BusinessRejectRefID) : null;
            if (id is not null && _waiting.TryRemove(id, out var tcs))
            {
                tcs.TrySetResult(message);
            }
        }
    }
}
