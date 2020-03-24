﻿namespace BDInSelfLove.Services.Models.Post
{
    using System;
    using System.Collections.Generic;
    using BDInSelfLove.Services.Mapping;
    using BDInSelfLove.Services.Models.Category;
    using BDInSelfLove.Services.Models.Comment;
    using BDInSelfLove.Services.Models.User;

    public class PostServiceModel : IMapTo<Data.Models.Post>, IMapFrom<Data.Models.Post>
    {
        public PostServiceModel()
        {
            this.Comments = new HashSet<CommentServiceModel>();
        }

        public int Id { get; set; }

        public DateTime CreatedOn { get; set; }

        public string Title { get; set; }

        public string Content { get; set; }

        public string UserId { get; set; }

        public ApplicationUserServiceModel User { get; set; }

        public ICollection<CommentServiceModel> Comments { get; set; }

        public int CategoryId { get; set; }

        public CategoryServiceModel Category { get; set; }

    }
}
