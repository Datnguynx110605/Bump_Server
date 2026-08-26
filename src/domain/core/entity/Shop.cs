namespace domain;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; }
    public string Address { get; private set; }
    public Amount Rate { get; private set; } = Amount.Create(0).Value;
    private List<Post> _posts = new();
    public IReadOnlyCollection<Post> Posts => _posts.AsReadOnly();
    private Shop() {}
    private Shop(
        string name,
        string address)
    {
        Name = name;
        Address = address;
    }

    public static DomainResult<Shop> Create(string name, string address)
    {
        if(string.IsNullOrWhiteSpace(name))
            return DomainResult<Shop>.Failure("Shop name is required");

        if(string.IsNullOrWhiteSpace(address))
            return DomainResult<Shop>.Failure("Shop address is required");

        Shop shop = new Shop(name, address);

        return DomainResult<Shop>.Success(shop);
    }

    public DomainResult AddPost(Post post)
    {
        if(_posts.Any(x => x.Id == post.Id))
            return DomainResult.Failure("Already contains post");

        _posts.Add(post);

        return DomainResult.Success();
    }

    public DomainResult RemovePost(Guid postId)
    {
        var post = _posts.FirstOrDefault(x => x.Id == postId);
        if(post is null)
            return DomainResult.Failure("Post not found");

        _posts.Remove(post);

        return DomainResult.Success();
    }
}
