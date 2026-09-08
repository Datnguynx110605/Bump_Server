namespace app;

// Each inverse pair (follow/unfollow, react/unreact, save/unsave) shares one queue: a
// single queue with prefetchCount 1 is FIFO, so the two operations apply in the order the
// user performed them. Separate queues are consumed concurrently and reorder.
public sealed record QueueName
{
    public static string Follow = "follow";
    public static string ReactPost = "react-post";
    public static string ReactComment = "react-comment";
    public static string CreateComment = "create-comment";
    public static string RecalculateShopRate = "recalculate-shop-rate";
    public static string SavePost = "save-post";
    public static string SaveShop = "save-shop";
    public static string DeleteAccount = "delete-account";
    public static string DeletePost = "delete-post";
    public static string DeleteComment = "delete-comment";
}
