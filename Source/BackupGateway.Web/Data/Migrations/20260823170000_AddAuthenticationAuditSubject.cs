using BackupGateway.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackupGateway.Web.Data.Migrations;

[DbContext(typeof(BackupGatewayDbContext))]
[Migration("20260823170000_AddAuthenticationAuditSubject")]
public sealed class AddAuthenticationAuditSubject : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "subject_client_id",
            table: "audit_events",
            type: "uuid",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "subject_client_id",
            table: "audit_events");
    }
}
