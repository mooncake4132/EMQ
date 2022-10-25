﻿using FluentMigrator;

namespace BlazorApp1.Server.Migrations;

[Migration(20221025_007)]
public class AddTableMusic_Source_Music : Migration
{
    private string tableName = "music_source_music";

    public override void Up()
    {
        Create.Table(tableName)
            .WithColumn("music_source_id").AsInt32().PrimaryKey().ForeignKey("music_source", "id")
            .WithColumn("music_id").AsInt32().PrimaryKey().ForeignKey("music", "id")
            .WithColumn("type").AsInt32().NotNullable()
            ;
    }

    public override void Down()
    {
        Delete.Table(tableName);
    }
}