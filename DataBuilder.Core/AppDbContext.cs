using DataBuilder.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Core;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<QAPair> QAPairs => Set<QAPair>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.Project)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Chunk>()
            .HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QAPair>()
            .HasOne(q => q.Chunk)
            .WithMany(c => c.QAPairs)
            .HasForeignKey(q => q.ChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引
        modelBuilder.Entity<Document>()
            .HasIndex(d => d.ProjectId);

        modelBuilder.Entity<Chunk>()
            .HasIndex(c => c.DocumentId);

        modelBuilder.Entity<QAPair>()
            .HasIndex(q => q.ChunkId);
    }
}
