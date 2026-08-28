namespace app;

public sealed record QueueName
{
    public static string Follow = "follow";
    public static string Unfollow = "unfollow";
    public static string ReactPost = "react-post";
    public static string UnreactPost = "unreact-post";
    public static string ReactComment = "react-comment";
    public static string UnreactComment = "unreact-comment";
}
