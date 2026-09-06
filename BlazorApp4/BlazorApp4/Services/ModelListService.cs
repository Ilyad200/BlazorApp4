using BlazorApp4.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace BlazorApp4.Services
{
    public class ModelListService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ModelListService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            using var context = _contextFactory.CreateDbContext();
        }

        public event Action? OnChange;

        public async Task AddModel(string modelUrl)
        {
            modelUrl = modelUrl.Trim();
            if (string.IsNullOrWhiteSpace(modelUrl)) return;

            using var context = _contextFactory.CreateDbContext();

            if (await context.Models.AnyAsync(m => m.Name == modelUrl)) return; 
            
            var newModel = new Model { Name = modelUrl };
            await context.Models.AddAsync(newModel);
            await context.SaveChangesAsync();
            OnChange?.Invoke();
        }
        public async Task RemoveModel(string modelUrl)
        {
            modelUrl = modelUrl.Trim();
            if (string.IsNullOrWhiteSpace(modelUrl)) return;

            using var context = _contextFactory.CreateDbContext();

            var model = context.Models.FirstOrDefault(m => m.Name == modelUrl);
            if (model == null) return;

            context.Models.Remove(model);
            await context.SaveChangesAsync();
            OnChange?.Invoke();
        }
        public List<string> GetModels()
        {
            using var context = _contextFactory.CreateDbContext();

            var listModels = context.Models.Select(m => m.Name).ToList();

            if (listModels == null || listModels.Count() <= 0) return [];

            return listModels;
        }
    }
}
