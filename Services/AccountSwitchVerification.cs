namespace CloudLightBlizzard.Services;

public enum AccountSwitchVerificationState
{
    WaitingForBattleNet,
    WaitingForLogin,
    LoggedIn,
    LoginRequired,
    Unconfirmed,
}

public static class AccountSwitchVerification
{
    public static AccountSwitchVerificationState Evaluate(bool clientRunning, long? activeAccountId,
        long targetAccountId, DateTime nowUtc, DateTime deadlineUtc, BattleNetLoginEvidence evidence)
    {
        // Neither a restored local pointer nor a running process proves server authentication.
        // The current probe has NO verified positive, target/session-bound authentication source.
        // Keep LoggedIn reserved for such a source; never infer it from absence of errors.
        if (evidence == BattleNetLoginEvidence.RealAuthExpired) return AccountSwitchVerificationState.LoginRequired;
        if (nowUtc >= deadlineUtc) return AccountSwitchVerificationState.Unconfirmed;
        if (!clientRunning) return AccountSwitchVerificationState.WaitingForBattleNet;
        return AccountSwitchVerificationState.WaitingForLogin;
    }
}
