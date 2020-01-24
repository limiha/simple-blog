using System.Text.RegularExpressions;

namespace SimpleBlog.Extensions
{
    public static class StringExtensions
    {
        public static string FindFirstImage(this string blogcontent)
        {
            var firstimage = "/img/twitter-summary-large.png";

            //Look for all the img src tags...
            var urlRx = new Regex("<img.+?src=[\"'](.+?)[\"'].*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var matches = urlRx.Matches(blogcontent);

            if (matches != null && matches.Count > 0)
            {
                if (matches[0].Groups != null && matches[0].Groups.Count > 0)
                {
                    firstimage = matches[0].Groups[1].Value.Trim();
                }
            }

            return firstimage.Trim();
        }
    }
}
