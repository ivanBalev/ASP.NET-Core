﻿namespace BDInSelfLove.Web.ViewModels.Article
{
    using System;
    using System.Collections.Generic;

    using BDInSelfLove.Data.Models;
    using BDInSelfLove.Services.Mapping;
    using BDInSelfLove.Web.ViewModels.Comment;
    using Ganss.Xss;

    public class ArticleViewModel : IMapFrom<Article>
    {
        public int Id { get; set; }

        public string Title { get; set; }

        public DateTime CreatedOn { get; set; }

        public string Content { get; set; }

        public string SanitizedContent => new HtmlSanitizer().Sanitize(this.Content);

        public string ImageUrl { get; set; }

        public IList<CommentViewModel> Comments { get; set; }
    }
}
