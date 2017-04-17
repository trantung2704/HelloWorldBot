namespace DayNinjaBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemoveUnusedCreatedDate : DbMigration
    {
        public override void Up()
        {
            DropColumn("dbo.Tasks", "Created");
        }
        
        public override void Down()
        {
            AddColumn("dbo.Tasks", "Created", c => c.DateTimeOffset(precision: 7));
        }
    }
}
