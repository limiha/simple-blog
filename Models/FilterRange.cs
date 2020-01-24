using System;

namespace SimpleBlog.Models
{
    public class FilterRange
    {
        private DateTime start;
        public DateTime Start
        {
            get => start;
            set => start = value.ToUniversalTime();
        }

        private DateTime end;
        public DateTime End
        {
            get => end;
            set => end = value.ToUniversalTime();
        }
    }
}