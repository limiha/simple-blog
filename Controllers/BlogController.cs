using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using WilderMinds.MetaWeblog;
using SimpleBlog.Models;
using SimpleBlog.Extensions;
using Microsoft.AspNetCore.Http;

namespace SimpleBlog.Models
{
    public class BlogController : Controller
    {
        private IBlogService _blog;
        private IOptionsSnapshot<BlogSettings> _settings;

        public BlogController(IBlogService blog, IOptionsSnapshot<BlogSettings> settings)
        {
            _blog = blog;
            _settings = settings;
        }

        [Route("/{page:int?}")]
        [Route("/blog/{page:int?}")]
        [OutputCache(Profile = "default")]
        [HttpGet]
        public async Task<IActionResult> Index([FromRoute]int page = 0)
        {
            var posts = await _blog.GetPosts(_settings.Value.PostsPerPage, _settings.Value.PostsPerPage * page);
            ViewData["Title"] = _settings.Value.Name + " - " + _settings.Value.ShortDescription;
            ViewData["Description"] = _settings.Value.Description;
            ViewData["prev"] = $"/{page + 1}/";
            ViewData["next"] = $"/{(page <= 1 ? null : page - 1 + "/")}";
            return View("Views/Blog/Index.cshtml", posts);
        }

        [Route("/contact")]
        [Route("/blog/contact")]
        [OutputCache(Profile = "default")]
        public IActionResult Contact()
        {
            ViewData["Title"] = _settings.Value.Name + " - Contact me";
            ViewData["Description"] = "Contact information for Tim Heuer";
            return View("Views/Contact.cshtml");
        }

        [Route("/blog/contact.aspx")]
        public IActionResult RedirectContact()
        {
            return LocalRedirectPermanent("/contact");
        }

        [Route("/blog/search.aspx")]
        public IActionResult RedirectSearch(IDictionary<string,string> query)
        {
            return LocalRedirectPermanent($"/search?q={query["q"]}");
        }

        [Route("/search")]
        public IActionResult Search(string searchTerm)
        {
            ViewData["Title"] = _settings.Value.Name + " Search this site";
            ViewData["Description"] = "Search site";
            return View("Views/Blog/Search.cshtml");
        }

        [Route("/")]
        [Route("/blog/default.aspx")]
        [HttpGet]
        public IActionResult RedirectRoot()
        {
            return LocalRedirectPermanent("/blog");
        }
        
        [Route("/blog/tags/{category}/default.aspx")]
        public IActionResult RedirectTagUri(string category)
        {
            return LocalRedirectPermanent($"/blog/category/{category}");
        }

        [Route("/blog/archives.aspx")]
        public IActionResult RedirectArchives()
        {
            return LocalRedirectPermanent("/blog/categories");
        }

        [Route("/notfound")]
        [HttpGet]
        public IActionResult ContentNotFound()
        {
            ViewData["Title"] = _settings.Value.Name + " - Something went wrong!";
            ViewData["Description"] = "Something went wrong -- couldn't find what you asked.";
            Response.StatusCode = 404;

            return View("Views/NotFound.cshtml");
        }

        [Route("/blog/categories")]
        [OutputCache(Profile = "default")]
        public async Task<IActionResult> Categories()
        {
            var categories = await _blog.GetCategories();
            var categoryInfos = new List<CategoryInfo>();
            foreach (var item in categories)
            {
                categoryInfos.Add(new CategoryInfo() { categoryid = item, title = item });
            }

            ViewData["Title"] = _settings.Value.Name + " Browse articles by category";
            ViewData["Description"] = "Browse articles by category";
            return View("Views/Blog/Categories.cshtml", categoryInfos.OrderBy(o=>o.title).ToList());
        }

        [Route("/blog/category/{category}/{page:int?}")]
        [OutputCache(Profile = "default")]
        public async Task<IActionResult> Category(string category, int page = 0)
        {
            var posts = (await _blog.GetPostsByCategory(category.Replace("+","%20"))).Skip(_settings.Value.PostsPerPage * page).Take(_settings.Value.PostsPerPage);

            // if there are none for this category show sometihng
            if (posts.Count() < 1)
            {
                Response.StatusCode = StatusCodes.Status301MovedPermanently;
                ViewData["PostId"] = "0";
                return View("Views/Blog/BlankCategory.cshtml");
            }

            ViewData["Title"] = _settings.Value.Name + " " + category;
            ViewData["Description"] = $"Articles posted in the {category} category";
            ViewData["prev"] = $"/blog/category/{category}/{page + 1}/";
            ViewData["next"] = $"/blog/category/{category}/{(page <= 1 ? null : page - 1 + "/")}";
            return View("Views/Blog/Index.cshtml", posts);
        }

        [Route("/blog/{slug?}")]
        [Route("/blog/post-{slug}")]
        [OutputCache(Profile = "default")]
        [HttpGet]
        public async Task<IActionResult> Post(string slug)
        {
            var post = await _blog.GetPostBySlug(slug);

            if (post != null)
            {
                return View(post);
            }

            return View("Views/NotFound.cshtml");
        }

        [Route("/blog/edit/{id?}")]
        [HttpGet, Authorize]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return View(new Post());
            }

            var post = await _blog.GetPostById(id);

            if (post != null)
            {
                return View(post);
            }

            return NotFound();
        }

        [Route("/blog/{slug?}")]
        [HttpPost, Authorize, AutoValidateAntiforgeryToken]
        public async Task<IActionResult> UpdatePost(Post post)
        {
            if (!ModelState.IsValid)
            {
                return View("Edit", post);
            }

            var existing = await _blog.GetPostById(post.ID) ?? post;
            string categories = Request.Form["categories"];

            existing.Categories = categories.Split(",", StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToLowerInvariant()).ToList();
            existing.Title = post.Title.Trim();
            existing.Slug = post.Slug.Trim();
            existing.Slug = !string.IsNullOrWhiteSpace(post.Slug) ? post.Slug.Trim() : Models.Post.CreateSlug(post.Title);
            existing.IsPublished = post.IsPublished;
            existing.Content = post.Content.Trim();
            existing.Excerpt = post.Excerpt.Trim();
            existing.PostImage = post.Content.FindFirstImage();

            await _blog.SavePost(existing);

            await SaveFilesToDisk(existing);

            return Redirect(post.GetLink());
        }

        private async Task SaveFilesToDisk(Post post)
        {
            //var imgRegex = new Regex("<img[^>].+ />", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var imgRegex = new Regex("(<img.*?)(src=[\\\"|'])(?<src>.*?)([\\\"|'].*?[/]?>)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var base64Regex = new Regex("data:[^/]+/(?<ext>[a-z]+);base64,(?<base64>.+)", RegexOptions.IgnoreCase);

            foreach (Match match in imgRegex.Matches(post.Content))
            {
                // fix-up for enabling support in HTML5 image tags (void tags)
                var fixedUpMatch = match.Value;
                if (!fixedUpMatch.EndsWith("/>")) fixedUpMatch = fixedUpMatch.Replace(">", "/>");

                XmlDocument doc = new XmlDocument();
                doc.LoadXml("<root>" + fixedUpMatch + "</root>");

                var img = doc.FirstChild.FirstChild;
                var srcNode = img.Attributes["src"];
                var fileNameNode = img.Attributes["data-filename"];

                // The HTML editor creates base64 DataURIs which we'll have to convert to image files on disk
                if (srcNode != null && fileNameNode != null)
                {
                    var base64Match = base64Regex.Match(srcNode.Value);
                    if (base64Match.Success)
                    {
                        byte[] bytes = Convert.FromBase64String(base64Match.Groups["base64"].Value);
                        srcNode.Value = await _blog.SaveFile(bytes, fileNameNode.Value).ConfigureAwait(false);

                        img.Attributes.Remove(fileNameNode);
                        post.Content = post.Content.Replace(match.Value, img.OuterXml);
                    }
                }
            }
        }

        [Route("/blog/deletepost/{id}")]
        [HttpPost, Authorize, AutoValidateAntiforgeryToken]
        public async Task<IActionResult> DeletePost(string id)
        {
            var existing = await _blog.GetPostById(id);

            if (existing != null)
            {
                await _blog.DeletePost(existing);
                return Redirect("/");
            }

            return NotFound();
        }

        [Route("/blog/comment/{postId}")]
        [HttpPost]
        public async Task<IActionResult> AddComment(string postId, Comment comment)
        {
            var post = await _blog.GetPostById(postId);

            if (!ModelState.IsValid)
            {
                return View("Post", post);
            }

            if (post == null || !post.AreCommentsOpen(_settings.Value.CommentsCloseAfterDays))
            {
                return NotFound();
            }

            comment.IsAdmin = User.Identity.IsAuthenticated;
            comment.Content = comment.Content.Trim();
            comment.Author = comment.Author.Trim();
            comment.Email = comment.Email.Trim();

            // the website form key should have been removed by javascript
            // unless the comment was posted by a spam robot
            if (!Request.Form.ContainsKey("website"))
            {
                post.Comments.Add(comment);
                await _blog.SavePost(post);
            }

            return Redirect(post.GetLink() + "#" + comment.ID);
        }

        [Route("/blog/comment/{postId}/{commentId}")]
        [Authorize]
        public async Task<IActionResult> DeleteComment(string postId, string commentId)
        {
            var post = await _blog.GetPostById(postId);

            if (post == null)
            {
                return NotFound();
            }

            var comment = post.Comments.FirstOrDefault(c => c.ID.Equals(commentId, StringComparison.OrdinalIgnoreCase));

            if (comment == null)
            {
                return NotFound();
            }

            post.Comments.Remove(comment);
            await _blog.SavePost(post);

            return Redirect(post.GetLink() + "#comments");
        }
    }
}
