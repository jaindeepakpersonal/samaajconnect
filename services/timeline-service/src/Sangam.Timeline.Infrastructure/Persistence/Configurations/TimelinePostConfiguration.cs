using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Infrastructure.Persistence.Configurations;

public sealed class TimelinePostConfiguration : IEntityTypeConfiguration<TimelinePost>
{
    public void Configure(EntityTypeBuilder<TimelinePost> builder)
    {
        builder.ToTable("timeline_posts");
        builder.HasKey(p => p.Id);

        // Domain-assigned. Left as EF's default a child added to a tracked
        // parent comes back Modified rather than Added and the save fails
        // against a row that was never there - the trap this repo has hit
        // twice, on Family and again on UserRole.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Body).IsRequired().HasMaxLength(5000);
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        // The feed: this Samaaj's approved posts, newest first.
        builder.HasIndex(p => new { p.TenantId, p.Status, p.CreatedAt });

        // "My posts", which the feed merges in so a member can see their own
        // pending ones.
        builder.HasIndex(p => new { p.TenantId, p.AuthorMemberId });

        builder.HasMany(p => p.Comments)
            .WithOne()
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Reactions)
            .WithOne()
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ModerationActions)
            .WithOne()
            .HasForeignKey(a => a.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var navigation in new[] { nameof(TimelinePost.Comments), nameof(TimelinePost.Reactions), nameof(TimelinePost.ModerationActions) })
        {
            builder.Metadata.FindNavigation(navigation)!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        }

        builder.Ignore(p => p.DomainEvents);
    }
}

public sealed class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.ToTable("post_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Body).IsRequired().HasMaxLength(2000);
        builder.HasIndex(c => c.PostId);
    }
}

public sealed class PostReactionConfiguration : IEntityTypeConfiguration<PostReaction>
{
    public void Configure(EntityTypeBuilder<PostReaction> builder)
    {
        builder.ToTable("post_reactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);

        // One reaction per member per post. The aggregate enforces it too; this
        // is what holds if two requests arrive at once.
        builder.HasIndex(r => new { r.PostId, r.MemberId }).IsUnique();
    }
}

public sealed class ModerationActionConfiguration : IEntityTypeConfiguration<ModerationAction>
{
    public void Configure(EntityTypeBuilder<ModerationAction> builder)
    {
        builder.ToTable("moderation_actions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.HasIndex(a => a.PostId);
    }
}
