﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using Cassandra;
using EFCore.Cassandra.Samples.Models;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EFCore.Cassandra.Samples.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cv");

            migrationBuilder.CreateTable(
                name: "applicants",
                schema: "cv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    Long = table.Column<long>(type: "bigint", nullable: false),
                    Bool = table.Column<bool>(type: "boolean", nullable: false),
                    Decimal = table.Column<decimal>(type: "decimal", nullable: false),
                    Double = table.Column<double>(type: "double", nullable: false),
                    Float = table.Column<float>(type: "float", nullable: false),
                    Integer = table.Column<int>(type: "int", nullable: false),
                    SmallInt = table.Column<short>(type: "smallint", nullable: false),
                    DateTimeOffset = table.Column<DateTimeOffset>(type: "timestamp", nullable: false),
                    TimeUuid = table.Column<TimeUuid>(type: "uuid", nullable: false),
                    Sbyte = table.Column<sbyte>(type: "tinyint", nullable: false),
                    BigInteger = table.Column<BigInteger>(type: "varint", nullable: false),
                    Blob = table.Column<byte[]>(type: "blob", nullable: true),
                    LocalDate = table.Column<LocalDate>(type: "date", nullable: true),
                    Ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    LocalTime = table.Column<LocalTime>(type: "time", nullable: true),
                    Lst = table.Column<IList<string>>(type: "list<text>", nullable: true),
                    LstInt = table.Column<IList<int>>(type: "list<int>", nullable: true),
                    Dic = table.Column<IDictionary<string, string>>(type: "map<text,text>", nullable: true),
                    Phones = table.Column<ApplicantPhone[]>(type: "set<frozen<applicant_addr>>", nullable: true),
                    Address = table.Column<ApplicantAddress>(type: "applicant_addr", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applicants", x => new { x.id, x.Order });
                });

            migrationBuilder.CreateUserDefinedType(
                name: "applicant_addr",
                schema: "cv",
                columns: table => new
                {
                    City = table.Column<string>(nullable: true),
                    StreetNumber = table.Column<int>(nullable: false)
                });

            migrationBuilder.CreateUserDefinedType(
                name: "applicant_phone",
                schema: "cv",
                columns: table => new
                {
                    IsMobile = table.Column<bool>(nullable: false),
                    PhoneNumber = table.Column<string>(nullable: true)
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applicants",
                schema: "cv");

            migrationBuilder.EnsureSchema(
                name: "cv");

            migrationBuilder.DropUserDefinedType(
                name: "applicant_addr",
                schema: "cv");

            migrationBuilder.DropUserDefinedType(
                name: "applicant_phone",
                schema: "cv");
        }
    }
}
