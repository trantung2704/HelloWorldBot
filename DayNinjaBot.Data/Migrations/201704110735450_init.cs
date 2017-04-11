namespace DayNinjaBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class init : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Tasks",
                c => new
                    {
                        Id = c.Long(nullable: false, identity: true),
                        ForUserId = c.Int(),
                        AddedByUserId = c.String(),
                        JobId = c.Long(),
                        Description = c.String(),
                        PublicNote = c.String(),
                        PrivateNote = c.String(),
                        Created = c.DateTimeOffset(precision: 7),
                        ForDate = c.DateTimeOffset(precision: 7),
                        PosNo = c.Int(),
                        UnitMins = c.Int(nullable: false),
                        RestMins = c.Int(nullable: false),
                        UnitsEst = c.Int(),
                        UnitsAct = c.Int(),
                        HourlyRate = c.Decimal(precision: 18, scale: 2),
                        DayMoveCount = c.Int(),
                        SplitOfTaskId = c.Long(),
                        Updated = c.DateTimeOffset(precision: 7),
                        Done = c.DateTimeOffset(precision: 7),
                        TotalTime = c.Time(precision: 7),
                        UserId = c.String(),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "dbo.TimeLogs",
                c => new
                    {
                        Id = c.Long(nullable: false, identity: true),
                        StartTime = c.DateTimeOffset(nullable: false, precision: 7),
                        EndTime = c.DateTimeOffset(nullable: false, precision: 7),
                        TaskId = c.Long(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Tasks", t => t.TaskId, cascadeDelete: true)
                .Index(t => t.TaskId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.TimeLogs", "TaskId", "dbo.Tasks");
            DropIndex("dbo.TimeLogs", new[] { "TaskId" });
            DropTable("dbo.TimeLogs");
            DropTable("dbo.Tasks");
        }
    }
}
