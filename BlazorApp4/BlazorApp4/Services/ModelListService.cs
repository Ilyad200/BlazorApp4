using BlazorApp4.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace BlazorApp4.Services
{
    // Общий список "доступных моделей" для выпадающего списка в ModelSelector.
    // Пока хранится только в памяти (как PinService) — при перезапуске сервера
    // сбрасывается к дефолтному набору. Если нужно, чтобы список моделей
    // переживал перезапуск, его несложно перенести в БД по тому же принципу,
    // что и остальные сущности (отдельная таблица ModelDb + DbSet в
    // ApplicationDbContext).
    public class ModelListService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ModelListService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            using var context = _contextFactory.CreateDbContext();
        }
        public List<string> Models { get; } = new()
        {
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "nvidia/nemotron-3-nano-30b-a3b:free",
            "nvidia/nemotron-3-super-120b-a12b:free",
            "poolside/laguna-xs-2.1:free",
            "cohere/north-mini-code:free",
            "dots-studio/dots-3-note-preview:free"
        };

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
    }
}
