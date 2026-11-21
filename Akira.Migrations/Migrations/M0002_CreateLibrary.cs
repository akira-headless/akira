using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(2)]
public class M0002_CreateLibrary : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("media_items").Exists())
        {
            Create.Table("media_items")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("type").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("parent_id").AsGuid().Nullable()
                    .ForeignKey("fk_media_items_parent_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("identity_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("title").AsCustom("TEXT").NotNullable()
                .WithColumn("sort_title").AsCustom("TEXT").NotNullable()
                .WithColumn("year").AsInt32().Nullable()
                .WithColumn("overview").AsCustom("TEXT").Nullable()
                .WithColumn("season_number").AsInt32().Nullable()
                .WithColumn("episode_number").AsInt32().Nullable()
                .WithColumn("air_date").AsDate().Nullable()
                .WithColumn("runtime_minutes").AsInt32().Nullable()
                .WithColumn("tmdb_id").AsInt32().Nullable()
                .WithColumn("tvdb_id").AsInt32().Nullable()
                .WithColumn("imdb_id").AsCustom("VARCHAR(20)").Nullable()
                .WithColumn("original_language").AsCustom("VARCHAR(3)").Nullable()
                .WithColumn("added_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_media_items_parent_id", "media_items", "parent_id", "media_items");
        }

        if (!Schema.Table("media_items").Index("ux_media_items_identity_key").Exists())
        {
            Create.Index("ux_media_items_identity_key").OnTable("media_items")
                .OnColumn("identity_key").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("media_items").Index("ix_media_items_parent_id").Exists())
        {
            Create.Index("ix_media_items_parent_id").OnTable("media_items")
                .OnColumn("parent_id").Ascending();
        }

        if (!Schema.Table("arr_links").Exists())
        {
            Create.Table("arr_links")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_item_id").AsGuid().NotNullable()
                    .ForeignKey("fk_arr_links_media_item_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("instance_key").AsCustom("VARCHAR(32)").NotNullable()
                .WithColumn("arr_id").AsInt32().Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_arr_links_media_item_id", "arr_links", "media_item_id", "media_items");
        }

        if (!Schema.Table("arr_links").Index("ux_arr_links_instance_key_media_item_id").Exists())
        {
            Create.Index("ux_arr_links_instance_key_media_item_id").OnTable("arr_links")
                .OnColumn("instance_key").Ascending()
                .OnColumn("media_item_id").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("media_files").Exists())
        {
            Create.Table("media_files")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_item_id").AsGuid().NotNullable()
                    .ForeignKey("fk_media_files_media_item_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("instance_key").AsCustom("VARCHAR(32)").NotNullable()
                .WithColumn("path").AsCustom("TEXT").NotNullable()
                .WithColumn("size_bytes").AsInt64().NotNullable()
                .WithColumn("container").AsCustom("VARCHAR(20)").Nullable()
                .WithColumn("arr_file_id").AsInt32().NotNullable()
                .WithColumn("added_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_media_files_media_item_id", "media_files", "media_item_id", "media_items");
        }

        if (!Schema.Table("media_files").Index("ux_media_files_media_item_id_instance_key").Exists())
        {
            Create.Index("ux_media_files_media_item_id_instance_key").OnTable("media_files")
                .OnColumn("media_item_id").Ascending()
                .OnColumn("instance_key").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("media_images").Exists())
        {
            Create.Table("media_images")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_item_id").AsGuid().NotNullable()
                    .ForeignKey("fk_media_images_media_item_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("instance_key").AsCustom("VARCHAR(32)").NotNullable()
                .WithColumn("kind").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("arr_id").AsInt32().Nullable()
                .WithColumn("arr_file_name").AsCustom("VARCHAR(100)").Nullable()
                .WithColumn("remote_url").AsCustom("TEXT").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_media_images_media_item_id", "media_images", "media_item_id", "media_items");
        }

        if (!Schema.Table("media_images").Index("ux_media_images_media_item_id_kind_instance_key").Exists())
        {
            Create.Index("ux_media_images_media_item_id_kind_instance_key").OnTable("media_images")
                .OnColumn("media_item_id").Ascending()
                .OnColumn("kind").Ascending()
                .OnColumn("instance_key").Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
        Delete.Table("media_images");
        Delete.Table("media_files");
        Delete.Table("arr_links");
        Delete.Table("media_items");
    }
}
