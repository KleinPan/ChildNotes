using ChildNotes.Core.Constants;
using ChildNotes.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Data;

public class ChildNotesDbContext : DbContext
{
    public ChildNotesDbContext(DbContextOptions<ChildNotesDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Baby> Babies => Set<Baby>();
    public DbSet<BabyMember> BabyMembers => Set<BabyMember>();
    public DbSet<ChildRecord> ChildRecords => Set<ChildRecord>();
    public DbSet<UserPoints> UserPoints => Set<UserPoints>();
    public DbSet<SignInRecord> SignInRecords => Set<SignInRecord>();
    public DbSet<TaskRecord> TaskRecords => Set<TaskRecord>();
    public DbSet<LotteryActivity> LotteryActivities => Set<LotteryActivity>();
    public DbSet<LotteryParticipation> LotteryParticipations => Set<LotteryParticipation>();
    public DbSet<LotteryPrize> LotteryPrizes => Set<LotteryPrize>();
    public DbSet<IpBlacklist> IpBlacklist => Set<IpBlacklist>();
    public DbSet<AiAnalysisRecord> AiAnalysisRecords => Set<AiAnalysisRecord>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<AdminLotteryActivity> AdminLotteryActivities => Set<AdminLotteryActivity>();
    public DbSet<AdminLotteryPrize> AdminLotteryPrizes => Set<AdminLotteryPrize>();
    public DbSet<Milestone> Milestones => Set<Milestone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.Username).HasColumnName("username").IsRequired().HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.NickName).HasColumnName("nick_name").HasMaxLength(64);
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
            e.Property(x => x.Gender).HasColumnName("gender");
            e.Property(x => x.ReferrerUserId).HasColumnName("referrer_user_id").HasColumnType("uuid");
            e.Property(x => x.ReferrerBoundAt).HasColumnName("referrer_bound_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.ReferrerUserId);
        });

        modelBuilder.Entity<Baby>(e =>
        {
            e.ToTable("baby");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Avatar).HasColumnName("avatar");
            e.Property(x => x.Gender).HasColumnName("gender");
            e.Property(x => x.BirthDate).HasColumnName("birth_date");
            e.Property(x => x.Deleted).HasColumnName("deleted").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            // 软删除查询过滤器：默认隐藏已删除的宝宝（同步通道用 IgnoreQueryFilters 绕过）
            e.HasQueryFilter(x => !x.Deleted);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<BabyMember>(e =>
        {
            e.ToTable("baby_member");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.BabyId).HasColumnName("baby_id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.RoleCode).HasColumnName("role_code").IsRequired();
            e.Property(x => x.RoleName).HasColumnName("role_name").IsRequired();
            e.Property(x => x.IsOwner).HasColumnName("is_owner");
            e.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue(StatusConstants.BabyMember.Active);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.BabyId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<ChildRecord>(e =>
        {
            e.ToTable("child_record");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.BabyId).HasColumnName("baby_id").HasColumnType("uuid");
            e.Property(x => x.RecordType).HasColumnName("record_type").IsRequired().HasMaxLength(32);
            e.Property(x => x.RecordSubType).HasColumnName("record_sub_type").HasMaxLength(32);
            e.Property(x => x.RecordDate).HasColumnName("record_date");
            e.Property(x => x.RecordTime).HasColumnName("record_time");
            e.Property(x => x.AmountMl).HasColumnName("amount_ml");
            e.Property(x => x.DurationSec).HasColumnName("duration_sec");
            e.Property(x => x.LeftDurationSec).HasColumnName("left_duration_sec");
            e.Property(x => x.RightDurationSec).HasColumnName("right_duration_sec");
            e.Property(x => x.AbnormalFlag).HasColumnName("abnormal_flag");
            e.Property(x => x.TemperatureValue).HasColumnName("temperature_value").HasColumnType("decimal(5,2)");
            e.Property(x => x.HeightCm).HasColumnName("height_cm").HasColumnType("decimal(6,2)");
            e.Property(x => x.WeightKg).HasColumnName("weight_kg").HasColumnType("decimal(6,3)");
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
            e.Property(x => x.Deleted).HasColumnName("deleted").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasQueryFilter(x => !x.Deleted);
            e.HasIndex(x => new { x.UserId, x.RecordDate, x.RecordType });
            e.HasIndex(x => new { x.BabyId, x.RecordDate });
            e.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<UserPoints>(e =>
        {
            e.ToTable("user_points");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.Points).HasColumnName("points");
            e.Property(x => x.TotalEarned).HasColumnName("total_earned");
            e.Property(x => x.TotalSpent).HasColumnName("total_spent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.UserId).IsUnique();
        });

        modelBuilder.Entity<SignInRecord>(e =>
        {
            e.ToTable("sign_in_record");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.SignDate).HasColumnName("sign_date");
            e.Property(x => x.SignTime).HasColumnName("sign_time");
            e.Property(x => x.ContinuousDays).HasColumnName("continuous_days");
            e.Property(x => x.CycleDay).HasColumnName("cycle_day");
            e.Property(x => x.RewardPoints).HasColumnName("reward_points");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.UserId, x.SignDate }).IsUnique();
        });

        modelBuilder.Entity<TaskRecord>(e =>
        {
            e.ToTable("task_record");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.TaskType).HasColumnName("task_type");
            e.Property(x => x.TaskKey).HasColumnName("task_key");
            e.Property(x => x.RelatedUserId).HasColumnName("related_user_id").HasColumnType("uuid");
            e.Property(x => x.Points).HasColumnName("points");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.PayloadJson).HasColumnName("payload_json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.TaskType, x.RelatedUserId }).IsUnique();
        });

        modelBuilder.Entity<LotteryActivity>(e =>
        {
            e.ToTable("lottery_activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CoverImage).HasColumnName("cover_image");
            e.Property(x => x.StartTime).HasColumnName("start_time");
            e.Property(x => x.DrawTime).HasColumnName("draw_time");
            e.Property(x => x.CostPoints).HasColumnName("cost_points");
            e.Property(x => x.ParticipantCount).HasColumnName("participant_count");
            e.Property(x => x.WinnerCount).HasColumnName("winner_count");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<LotteryParticipation>(e =>
        {
            e.ToTable("lottery_participation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.ActivityId).HasColumnName("activity_id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.CostPoints).HasColumnName("cost_points");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.ActivityId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<LotteryPrize>(e =>
        {
            e.ToTable("lottery_prize");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.ActivityId).HasColumnName("activity_id").HasColumnType("uuid");
            e.Property(x => x.PrizeName).HasColumnName("prize_name");
            e.Property(x => x.PrizeIntro).HasColumnName("prize_intro");
            e.Property(x => x.PrizeImage).HasColumnName("prize_image");
            e.Property(x => x.PrizeCount).HasColumnName("prize_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.ActivityId);
        });

        modelBuilder.Entity<IpBlacklist>(e =>
        {
            e.ToTable("ip_blacklist");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.IpAddress).HasColumnName("ip_address");
            e.Property(x => x.TriggerMethod).HasColumnName("trigger_method");
            e.Property(x => x.TriggerPath).HasColumnName("trigger_path");
            e.Property(x => x.TriggerEndpoint).HasColumnName("trigger_endpoint");
            e.Property(x => x.RequestCount).HasColumnName("request_count");
            e.Property(x => x.WindowStartedAt).HasColumnName("window_started_at");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.IpAddress).IsUnique();
        });

        modelBuilder.Entity<AiAnalysisRecord>(e =>
        {
            e.ToTable("ai_analysis_record");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.BabyId).HasColumnName("baby_id").HasColumnType("uuid");
            e.Property(x => x.BabyName).HasColumnName("baby_name");
            e.Property(x => x.RangeStartDate).HasColumnName("range_start_date");
            e.Property(x => x.RangeEndDate).HasColumnName("range_end_date");
            e.Property(x => x.SourceText).HasColumnName("source_text");
            e.Property(x => x.SkillPrompt).HasColumnName("skill_prompt");
            e.Property(x => x.AnalysisText).HasColumnName("analysis_text");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.UserId, x.BabyId, x.RangeStartDate, x.RangeEndDate }).IsUnique();
            e.HasIndex(x => x.BabyId);
        });

        modelBuilder.Entity<AdminAccount>(e =>
        {
            e.ToTable("admin_account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Token).HasColumnName("token");
            e.Property(x => x.TokenExpireAt).HasColumnName("token_expire_at");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Token);
        });

        modelBuilder.Entity<AdminLotteryActivity>(e =>
        {
            e.ToTable("admin_lottery_activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CoverImage).HasColumnName("cover_image");
            e.Property(x => x.StartTime).HasColumnName("start_time");
            e.Property(x => x.DrawTime).HasColumnName("draw_time");
            e.Property(x => x.CostPoints).HasColumnName("cost_points");
            e.Property(x => x.WinnerCount).HasColumnName("winner_count");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.PublishTime).HasColumnName("publish_time");
            e.Property(x => x.CreatedBy).HasColumnName("created_by").HasColumnType("uuid");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasColumnType("uuid");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AdminLotteryPrize>(e =>
        {
            e.ToTable("admin_lottery_prize");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.ActivityId).HasColumnName("activity_id").HasColumnType("uuid");
            e.Property(x => x.PrizeName).HasColumnName("prize_name");
            e.Property(x => x.PrizeIntro).HasColumnName("prize_intro");
            e.Property(x => x.PrizeImage).HasColumnName("prize_image");
            e.Property(x => x.PrizeCount).HasColumnName("prize_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.ActivityId);
        });

        modelBuilder.Entity<Milestone>(e =>
        {
            e.ToTable("milestone");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.BabyId).HasColumnName("baby_id").HasColumnType("uuid");
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.RecordDate).HasColumnName("record_date");
            e.Property(x => x.PhotosJson).HasColumnName("photos_json").IsRequired().HasDefaultValue("[]");
            e.Property(x => x.Deleted).HasColumnName("deleted").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasQueryFilter(x => !x.Deleted);
            e.HasIndex(x => new { x.UserId, x.RecordDate });
            e.HasIndex(x => x.BabyId);
            e.HasIndex(x => x.UpdatedAt);
        });
    }
}
