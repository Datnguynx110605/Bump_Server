namespace app;

public sealed record QueueName
{
    public static string Follow = "follow";
    public static string Unfollow = "unfollow";
    public static string ReactPost = "react-post";
    public static string UnreactPost = "unreact-post";
    public static string ReactComment = "react-comment";
    public static string UnreactComment = "unreact-comment";
    public static string CreateComment = "create-comment";
    public static string RecalculateShopRate = "recalculate-shop-rate";
    public static string SavePost = "save-post";
    public static string UnsavePost = "unsave-post";
    public static string SaveShop = "save-shop";
    public static string UnsaveShop = "unsave-shop";
    public static string DeleteAccount = "delete-account";
    public static string DeletePost = "delete-post";
}
