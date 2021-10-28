﻿namespace BDInSelfLove.Services.Data.Video
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using BDInSelfLove.Data.Common.Repositories;
    using BDInSelfLove.Data.Models;
    using BDInSelfLove.Services.Data.Comments;
    using Microsoft.EntityFrameworkCore;
    using NinjaNye.SearchExtensions;

    public class VideoService : IVideoService
    {
        private readonly IDeletableEntityRepository<Video> videosRepository;
        private readonly ICommentService commentService;

        public VideoService(
            IDeletableEntityRepository<Video> videosRepository,
            ICommentService commentService)
        {
            this.videosRepository = videosRepository;
            this.commentService = commentService;
        }

        public async Task<string> Create(Video video)
        {
            await this.videosRepository.AddAsync(video);
            await this.videosRepository.SaveChangesAsync();
            return video.Title.ToLower().Replace(' ', '-');
        }

        public async Task<Video> GetBySlug(string slug)
        {
            var video = await this.videosRepository.All()
               .Where(a => a.Title.ToLower() == slug.Replace('-', ' '))
              .Select(x => new Video
              {
                  Id = x.Id,
                  Url = x.Url,
                  Title = x.Title,
                  CreatedOn = x.CreatedOn,
                  Comments = new List<Comment>(x.Comments.Select(c => new Comment
                  {
                      Id = c.Id,
                      Content = c.Content,
                      UserId = c.UserId,
                      User = new ApplicationUser
                      {
                          UserName = c.User.UserName,
                          ProfilePhoto = c.User.ProfilePhoto,
                      },
                      ArticleId = c.ArticleId,
                      ParentCommentId = c.ParentCommentId,
                      CreatedOn = c.CreatedOn,
                      SubComments = new List<Comment>(),
                  })),
              })
                .FirstOrDefaultAsync();

            video.Comments = this.commentService.ArrangeCommentHierarchy(video.Comments);
            return video;
        }

        public IQueryable<Video> GetAll(int? take = null, int skip = 0, string searchString = null)
        {
            var query = this.videosRepository.All();


            if (searchString != null)
            {
                var searchItems = SearchHelper.GetSearchItems(searchString);
                query = query.Search(x => x.AssociatedTerms, x => x.Title).Containing(searchItems);
            }

            query = query.Distinct().OrderByDescending(a => a.CreatedOn).Skip(skip);

            if (take.HasValue)
            {
                query = query.Take(take.Value);
            }

            return query;
        }

        public async Task<int> Delete(int id)
        {
            var dbVideo = await this.videosRepository.All().SingleOrDefaultAsync(a => a.Id == id);
            this.videosRepository.Delete(dbVideo);
            return await this.videosRepository.SaveChangesAsync();
        }

        public IQueryable<Video> GetSideVideos(int videosCount, int videoId = 0)
        {
            var videos = this.videosRepository.All()
               .Where(v => v.Id != videoId)
               .OrderByDescending(a => a.CreatedOn)
               .Take(videosCount);

            return videos;
        }
    }
}
