using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>Banco de dados da própria IDE (SQLite).</summary>
public class IdeDbContext : DbContext
{
    public IdeDbContext(DbContextOptions<IdeDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<IdeDictionary> Dictionaries => Set<IdeDictionary>();
    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<EntityField> EntityFields => Set<EntityField>();
    public DbSet<EntityKey> EntityKeys => Set<EntityKey>();
    public DbSet<EntityRelation> EntityRelations => Set<EntityRelation>();
    public DbSet<GeneratedPage> GeneratedPages => Set<GeneratedPage>();
    public DbSet<DesignSession> DesignSessions => Set<DesignSession>();
    public DbSet<WizardRun> WizardRuns => Set<WizardRun>();
    public DbSet<AiProvider> AiProviders => Set<AiProvider>();
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<AiSkill> AiSkills => Set<AiSkill>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<IdePreference> Preferences => Set<IdePreference>();
    public DbSet<IdeSession> Sessions => Set<IdeSession>();
    public DbSet<GitSetting> GitSettings => Set<GitSetting>();
    public DbSet<McpToolLog> McpToolLogs => Set<McpToolLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Path).IsRequired().HasMaxLength(1000);
            e.HasIndex(x => x.Path).IsUnique();
        });

        b.Entity<IdeDictionary>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.HasMany(x => x.Entities).WithOne().HasForeignKey(x => x.DictionaryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Entity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.TableName).IsRequired().HasMaxLength(200);
            e.HasMany(x => x.Fields).WithOne().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Keys).WithOne().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EntityField>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
        });

        b.Entity<EntityKey>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        b.Entity<EntityRelation>(e =>
        {
            e.HasKey(x => x.Id);
            // navegações ligadas às FKs existentes (não cria colunas novas)
            e.HasOne(r => r.FromEntity!)
                .WithMany()
                .HasForeignKey(r => r.FromEntityId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.ToEntity!)
                .WithMany()
                .HasForeignKey(r => r.ToEntityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<GeneratedPage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectId, x.Route });
        });

        b.Entity<DesignSession>(e => e.HasKey(x => x.Id));
        b.Entity<WizardRun>(e => e.HasKey(x => x.Id));

        b.Entity<AiProvider>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        b.Entity<AiAgent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectId, x.Name });
        });

        b.Entity<AiSkill>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        b.Entity<AiConversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AiMessage>(e => e.HasKey(x => x.Id));


        b.Entity<GitSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId).IsUnique();
        });

        b.Entity<IdePreference>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
        });

        b.Entity<IdeSession>(e =>
        {
            // uma sessão por projeto (Key = caminho do projeto)
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(1000);
            e.Property(x => x.TabsJson).HasMaxLength(8000);
            e.Property(x => x.ActivePath).HasMaxLength(1000);
        });

        b.Entity<McpToolLog>(e => e.HasKey(x => x.Id));
    }
}
