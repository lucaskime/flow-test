using OrderGenerator.Application;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using Message = QuickFix.Message;

namespace OrderGenerator.Infrastructure.Fix;

internal sealed class InitiatorApplication(
    FixSessionMonitor monitor,
    PendingOrders pending,
    ILogger<InitiatorApplication> logger) : MessageCracker, IApplication
{
    public void OnCreate(SessionID sessionID) =>
        logger.LogInformation("FIX session created {SessionId}", sessionID);

    public void OnLogon(SessionID sessionID)
    {
        monitor.MarkLoggedOn(sessionID);
        logger.LogInformation("FIX logon {SessionId}", sessionID);
    }

    public void OnLogout(SessionID sessionID)
    {
        monitor.MarkLoggedOut(sessionID);
        logger.LogInformation("FIX logout {SessionId}", sessionID);
    }

    public void ToAdmin(Message message, SessionID sessionID) { }

    public void FromAdmin(Message message, SessionID sessionID) { }

    public void ToApp(Message message, SessionID sessionID) { }

    public void FromApp(Message message, SessionID sessionID) => Crack(message, sessionID);

    public void OnMessage(BusinessMessageReject reject, SessionID sessionID)
    {
        var text = reject.IsSetText() ? reject.Text.Value : "Business message reject";
        logger.LogWarning("BusinessMessageReject {Reason}: {Text}", reject.BusinessRejectReason.Value, text);

        if (reject.IsSetBusinessRejectRefID())
        {
            pending.Complete(new OrderOutcome(reject.BusinessRejectRefID.Value, OrderOutcomeStatus.Rejected, text));
        }
    }

    public void OnMessage(ExecutionReport report, SessionID sessionID)
    {
        var clOrdId = report.ClOrdID.Value;
        var execType = report.ExecType.Value;

        var outcome = execType switch
        {
            ExecType.NEW => new OrderOutcome(clOrdId, OrderOutcomeStatus.Accepted),
            ExecType.REJECTED => new OrderOutcome(clOrdId, OrderOutcomeStatus.Rejected,
                report.IsSetText() ? report.Text.Value : null),
            _ => null
        };

        if (outcome is null)
        {
            logger.LogWarning("Unexpected ExecType {ExecType} for {ClOrdId}", execType, clOrdId);
            return;
        }

        logger.LogInformation("ExecutionReport {ClOrdId} -> {Status}", clOrdId, outcome.Status);
        pending.Complete(outcome);
    }
}
