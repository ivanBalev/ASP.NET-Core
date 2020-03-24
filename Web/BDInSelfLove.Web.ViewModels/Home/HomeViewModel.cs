﻿namespace BDInSelfLove.Web.ViewModels.Home
{
    using System.Collections.Generic;

    using BDInSelfLove.Web.ViewModels.Article;

    public class HomeViewModel
    {
        public IEnumerable<ArticleViewModel> LastArticles { get; set; }

        public ArticleViewModel FeaturedArticle { get; set; }
    }
}
