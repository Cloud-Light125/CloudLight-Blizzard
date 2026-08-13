namespace BnetSwitch.Models;

public enum AccountRegionOverride
{
    Auto,
    China,
    International,
}

public sealed class AccountPreference
{
    public string CustomName { get; set; } = "";
    public string Remark { get; set; } = "";
    public AccountRegionOverride Region { get; set; } = AccountRegionOverride.Auto;
}
