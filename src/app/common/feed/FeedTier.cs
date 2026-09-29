namespace app;

// The sources the related feed blends. Declaration order is the cross-tier dedupe priority:
// a post reachable from two tiers is kept for whichever is declared first.
//
// Global is not a weighted tier. It is the backstop that runs only when the assembled page
// is still empty, which is what keeps a brand-new account from getting nothing at all.
internal enum FeedTier
{
    Nearby,
    Following,
    Affinity,
    Trusted,
    Global
}
