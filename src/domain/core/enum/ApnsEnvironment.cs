namespace domain;

// Which APNs host a device token is valid against. Stored per token rather than read from
// configuration because one deployment serves both: Xcode builds register sandbox tokens
// against the same server TestFlight builds register production tokens against.
public enum ApnsEnvironment
{
    Sandbox,
    Production
}
