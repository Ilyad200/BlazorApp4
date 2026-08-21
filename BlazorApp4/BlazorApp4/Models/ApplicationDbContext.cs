using Microsoft.EntityFrameworkCore;

namespace BlazorApp4.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        // DbSet-ы для всех сущностей
        public DbSet<ChatDb> Chats { get; set; }
        public DbSet<MessageDb> Messages { get; set; }
        public DbSet<VersionDb> Versions { get; set; }
        public DbSet<ChatSnapshot> ChatSnapshots { get; set; }
        public DbSet<SnapshotEntry> SnapshotEntries { get; set; }
        public DbSet<Model> Models { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 1. Настройка сущности ChatDb
            modelBuilder.Entity<ChatDb>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Name)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(c => c.CreatedAt)
                      .IsRequired()
                      .HasDefaultValueSql("CURRENT_TIMESTAMP"); // для PostgreSQL

                // Связь "один ко многим" с MessageDb (каскадное удаление)
                entity.HasMany(c => c.Messages)
                      .WithOne(m => m.Chat)
                      .HasForeignKey(m => m.ChatId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Индекс для быстрого поиска по имени (если нужно)
                entity.HasIndex(c => c.Name);
            });

            // 2. Настройка сущности MessageDb
            modelBuilder.Entity<MessageDb>(entity =>
            {
                entity.HasKey(m => m.Id);

                entity.Property(m => m.Role)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(m => m.CurrentVersionOrder)
                      .IsRequired();

                // Внешний ключ на текущую версию (может быть null)
                //entity.HasOne(m => m.GetCurrentVersion())
                //      .WithOne() // у VersionDb нет обратной ссылки на CurrentVersion
                //      .HasForeignKey<MessageDb>(m => m.CurrentVersionOrder)
                //      .OnDelete(DeleteBehavior.SetNull);

                // Индекс по ChatId для ускорения запросов
                entity.HasIndex(m => m.ChatId);
            });

            // 3. Настройка сущности VersionDb
            modelBuilder.Entity<VersionDb>(entity =>
            {
                entity.HasKey(v => v.Id);

                entity.Property(v => v.Content)
                      .IsRequired()
                      .HasColumnType("text"); // для PostgreSQL, чтобы гарантировать неограниченный размер

                entity.Property(v => v.Model)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(v => v.CreatedAt)
                      .IsRequired()
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Связь с MessageDb: удаление версий при удалении сообщения
                entity.HasOne(v => v.Message)
                      .WithMany(m => m.Versions)
                      .HasForeignKey(v => v.MessageId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Индексы для частых запросов
                entity.HasIndex(v => v.MessageId);
            });

            // MessageVersion
            //modelBuilder.Entity<VersionDb>(entity =>
            //{
            //    entity.HasKey(v => v.Id);
            //    entity.Property(v => v.Content).IsRequired().HasColumnType("text");
            //    entity.Property(v => v.Model).HasMaxLength(100).IsRequired();
            //    entity.Property(v => v.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            //    entity.HasIndex(v => v.MessageId);
            //    entity.HasIndex(v => v.CreatedAt);
            //});

            // Дополнительно: конфигурация для JSON-сериализации (опционально)
            // Если какие-то поля хранят сложные объекты, можно использовать
            // ValueConversion для сохранения как JSON в PostgreSQL.
            // Например, если Role - это enum:
            // entity.Property(m => m.Role)
            //       .HasConversion<string>()
            //       .HasMaxLength(50);
            modelBuilder.Entity<Model>().HasData(
                new Model { Id = 1, Name = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free" },
                new Model { Id = 2, Name = "nvidia/nemotron-3-nano-30b-a3b:free" },
                new Model { Id = 3, Name = "nvidia/nemotron-3-super-120b-a12b:free" },
                new Model { Id = 4, Name = "poolside/laguna-xs-2.1:free" },
                new Model { Id = 5, Name = "cohere/north-mini-code:free" },
                new Model { Id = 6, Name = "dots-studio/dots-3-note-preview:free" }
            );
            base.OnModelCreating(modelBuilder);
        }

        // Опционально: переопределение SaveChanges для автоматической установки CreatedAt
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is ChatDb || e.Entity is VersionDb)
                .Select(e => new { Entry = e, Entity = e.Entity });

            foreach (var entry in entries)
            {
                if (entry.Entry.State == EntityState.Added)
                {
                    if (entry.Entity is ChatDb chat)
                        chat.CreatedAt = DateTimeOffset.UtcNow;
                    else if (entry.Entity is VersionDb version)
                        version.CreatedAt = DateTimeOffset.UtcNow;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}