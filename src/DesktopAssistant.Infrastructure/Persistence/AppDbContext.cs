using DesktopAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DesktopAssistant.Infrastructure.Persistence;

/// <summary>
/// Контекст базы данных приложения
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<MessageNode> MessageNodes => Set<MessageNode>();
    public DbSet<AssistantProfile> AssistantProfiles => Set<AssistantProfile>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Conversation
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.SystemPrompt).HasMaxLength(10000);
            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne(e => e.AssistantProfile)
                  .WithMany(a => a.Conversations)
                  .HasForeignKey(e => e.AssistantProfileId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ActiveLeafNode)
                  .WithMany()
                  .HasForeignKey(e => e.ActiveLeafNodeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // MessageNode
        modelBuilder.Entity<MessageNode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.NodeType).HasConversion<string>();
            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Parent)
                  .WithMany(e => e.Children)
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ActiveChild)
                  .WithMany()
                  .HasForeignKey(e => e.ActiveChildId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.ParentId);
            entity.HasIndex(e => e.ActiveChildId);
        });

        // AssistantProfile
        modelBuilder.Entity<AssistantProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ModelId).HasMaxLength(200);
            entity.Property(e => e.BaseUrl).HasMaxLength(500);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // AppSettings
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasIndex(e => e.Key).IsUnique();
        });
    }
}
