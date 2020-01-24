namespace SimpleBlog
{
    public class BlogSettings
    {
        public string Name { get; set; } = "Your Blog Name";
        public string Description { get; set; } = "A short description of the blog";
        public string Owner { get; set; } = "Your Name";
        public int PostsPerPage { get; set; } = 5;
        public int CommentsCloseAfterDays { get; set; } = 10;
        public string ShortDescription { get; set; } = "An amazing site with lots of content";
    }
}
