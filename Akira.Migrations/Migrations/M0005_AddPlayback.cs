using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(5)]
public class M0005_AddPlayback : GuardedMigration
{
    public override void Up()
    {
        // No foreign key to media_items on purpose: watch state outlives a removed item
        if (!Schema.Table("watch_states").Exists())
        {
            Create.Table("watch_states")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_watch_states_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("identity_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("position_ms").AsInt64().NotNullable()
                .WithColumn("is_played").AsBoolean().NotNullable()
                .WithColumn("play_count").AsInt32().NotNullable()
                .WithColumn("last_played_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_watch_states_user_id", "watch_states", "user_id", "users");
        }

        if (!Schema.Table("watch_states").Index("ux_watch_states_user_id_identity_key").Exists())
        {
            Create.Index("ux_watch_states_user_id_identity_key").OnTable("watch_states")
                .OnColumn("user_id").Ascending()
                .OnColumn("identity_key").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("track_preferences").Exists())
        {
            Create.Table("track_preferences")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_track_preferences_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("scope").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("scope_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("audio_languages").AsCustom("VARCHAR(200)").Nullable()
                .WithColumn("prefer_original_language").AsBoolean().Nullable()
                .WithColumn("prefer_lossless").AsBoolean().Nullable()
                .WithColumn("exclude_commentary").AsBoolean().Nullable()
                .WithColumn("exclude_descriptive").AsBoolean().Nullable()
                .WithColumn("subtitle_mode").AsCustom("VARCHAR(20)").Nullable()
                .WithColumn("subtitle_languages").AsCustom("VARCHAR(200)").Nullable()
                .WithColumn("prefer_hearing_impaired").AsBoolean().Nullable()
                .WithColumn("remembered_audio").AsCustom("TEXT").Nullable()
                .WithColumn("remembered_subtitle").AsCustom("TEXT").Nullable()
                .WithColumn("remembered_subtitle_off").AsBoolean().Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_track_preferences_user_id", "track_preferences", "user_id", "users");
        }

        if (!Schema.Table("track_preferences").Index("ux_track_preferences_user_id_scope_scope_key").Exists())
        {
            Create.Index("ux_track_preferences_user_id_scope_scope_key").OnTable("track_preferences")
                .OnColumn("user_id").Ascending()
                .OnColumn("scope").Ascending()
                .OnColumn("scope_key").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("playback_sessions").Exists())
        {
            Create.Table("playback_sessions")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_playback_sessions_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("device_id").AsGuid().Nullable()
                    .ForeignKey("fk_playback_sessions_device_id", "devices", "id").OnDelete(Rule.Cascade)
                .WithColumn("media_item_id").AsGuid().NotNullable()
                    .ForeignKey("fk_playback_sessions_media_item_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_playback_sessions_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("identity_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("memory_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("memory_scope").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("position_ms").AsInt64().NotNullable()
                .WithColumn("is_paused").AsBoolean().NotNullable()
                .WithColumn("audio_stream_id").AsCustom("VARCHAR(32)").Nullable()
                .WithColumn("subtitle_stream_id").AsCustom("VARCHAR(32)").Nullable()
                .WithColumn("last_progress_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("ended_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_playback_sessions_user_id", "playback_sessions", "user_id", "users");
            AddForeignKeyIfMissing("fk_playback_sessions_device_id", "playback_sessions", "device_id", "devices");
            AddForeignKeyIfMissing("fk_playback_sessions_media_item_id", "playback_sessions", "media_item_id", "media_items");
            AddForeignKeyIfMissing("fk_playback_sessions_media_file_id", "playback_sessions", "media_file_id", "media_files");
        }

        if (!Schema.Table("playback_sessions").Index("ix_playback_sessions_user_id").Exists())
        {
            Create.Index("ix_playback_sessions_user_id").OnTable("playback_sessions").OnColumn("user_id").Ascending();
        }

        if (!Schema.Table("playback_sessions").Index("ix_playback_sessions_device_id").Exists())
        {
            Create.Index("ix_playback_sessions_device_id").OnTable("playback_sessions").OnColumn("device_id").Ascending();
        }

        if (!Schema.Table("playback_sessions").Index("ix_playback_sessions_media_item_id").Exists())
        {
            Create.Index("ix_playback_sessions_media_item_id").OnTable("playback_sessions").OnColumn("media_item_id").Ascending();
        }

        if (!Schema.Table("playback_sessions").Index("ix_playback_sessions_media_file_id").Exists())
        {
            Create.Index("ix_playback_sessions_media_file_id").OnTable("playback_sessions").OnColumn("media_file_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("playback_sessions");
        Delete.Table("track_preferences");
        Delete.Table("watch_states");
    }
}
