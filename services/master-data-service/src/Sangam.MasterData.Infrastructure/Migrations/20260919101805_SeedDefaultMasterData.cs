using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sangam.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "cities",
                columns: new[] { "id", "country_id", "is_active", "name", "state_id" },
                values: new object[,]
                {
                    { new Guid("e0000000-0000-0000-0000-000000000001"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Jaipur", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000002"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Udaipur", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000003"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Jodhpur", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000004"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Ajmer", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000005"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Kota", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000006"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Bikaner", new Guid("d0000000-0000-0000-0000-000000000021") },
                    { new Guid("e0000000-0000-0000-0000-000000000007"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Ahmedabad", new Guid("d0000000-0000-0000-0000-000000000007") },
                    { new Guid("e0000000-0000-0000-0000-000000000008"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Surat", new Guid("d0000000-0000-0000-0000-000000000007") },
                    { new Guid("e0000000-0000-0000-0000-000000000009"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Vadodara", new Guid("d0000000-0000-0000-0000-000000000007") },
                    { new Guid("e0000000-0000-0000-0000-000000000010"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Rajkot", new Guid("d0000000-0000-0000-0000-000000000007") },
                    { new Guid("e0000000-0000-0000-0000-000000000011"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Bhavnagar", new Guid("d0000000-0000-0000-0000-000000000007") },
                    { new Guid("e0000000-0000-0000-0000-000000000012"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Mumbai", new Guid("d0000000-0000-0000-0000-000000000014") },
                    { new Guid("e0000000-0000-0000-0000-000000000013"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Pune", new Guid("d0000000-0000-0000-0000-000000000014") },
                    { new Guid("e0000000-0000-0000-0000-000000000014"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Nagpur", new Guid("d0000000-0000-0000-0000-000000000014") },
                    { new Guid("e0000000-0000-0000-0000-000000000015"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Nashik", new Guid("d0000000-0000-0000-0000-000000000014") },
                    { new Guid("e0000000-0000-0000-0000-000000000016"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Indore", new Guid("d0000000-0000-0000-0000-000000000013") },
                    { new Guid("e0000000-0000-0000-0000-000000000017"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Bhopal", new Guid("d0000000-0000-0000-0000-000000000013") },
                    { new Guid("e0000000-0000-0000-0000-000000000018"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Gwalior", new Guid("d0000000-0000-0000-0000-000000000013") },
                    { new Guid("e0000000-0000-0000-0000-000000000019"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Ujjain", new Guid("d0000000-0000-0000-0000-000000000013") },
                    { new Guid("e0000000-0000-0000-0000-000000000020"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "New Delhi", new Guid("d0000000-0000-0000-0000-000000000032") },
                    { new Guid("e0000000-0000-0000-0000-000000000021"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Gurugram", new Guid("d0000000-0000-0000-0000-000000000008") },
                    { new Guid("e0000000-0000-0000-0000-000000000022"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Faridabad", new Guid("d0000000-0000-0000-0000-000000000008") },
                    { new Guid("e0000000-0000-0000-0000-000000000023"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Bengaluru", new Guid("d0000000-0000-0000-0000-000000000011") },
                    { new Guid("e0000000-0000-0000-0000-000000000024"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Mysuru", new Guid("d0000000-0000-0000-0000-000000000011") },
                    { new Guid("e0000000-0000-0000-0000-000000000025"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Chennai", new Guid("d0000000-0000-0000-0000-000000000023") },
                    { new Guid("e0000000-0000-0000-0000-000000000026"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Coimbatore", new Guid("d0000000-0000-0000-0000-000000000023") },
                    { new Guid("e0000000-0000-0000-0000-000000000027"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Kolkata", new Guid("d0000000-0000-0000-0000-000000000028") },
                    { new Guid("e0000000-0000-0000-0000-000000000028"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Lucknow", new Guid("d0000000-0000-0000-0000-000000000026") },
                    { new Guid("e0000000-0000-0000-0000-000000000029"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Kanpur", new Guid("d0000000-0000-0000-0000-000000000026") },
                    { new Guid("e0000000-0000-0000-0000-000000000030"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Agra", new Guid("d0000000-0000-0000-0000-000000000026") },
                    { new Guid("e0000000-0000-0000-0000-000000000031"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Varanasi", new Guid("d0000000-0000-0000-0000-000000000026") },
                    { new Guid("e0000000-0000-0000-0000-000000000032"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Hyderabad", new Guid("d0000000-0000-0000-0000-000000000024") },
                    { new Guid("e0000000-0000-0000-0000-000000000033"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Ludhiana", new Guid("d0000000-0000-0000-0000-000000000020") },
                    { new Guid("e0000000-0000-0000-0000-000000000034"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Amritsar", new Guid("d0000000-0000-0000-0000-000000000020") },
                    { new Guid("e0000000-0000-0000-0000-000000000035"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Chandigarh", new Guid("d0000000-0000-0000-0000-000000000030") },
                    { new Guid("e0000000-0000-0000-0000-000000000036"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Patna", new Guid("d0000000-0000-0000-0000-000000000004") },
                    { new Guid("e0000000-0000-0000-0000-000000000037"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Kochi", new Guid("d0000000-0000-0000-0000-000000000012") },
                    { new Guid("e0000000-0000-0000-0000-000000000038"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Thiruvananthapuram", new Guid("d0000000-0000-0000-0000-000000000012") },
                    { new Guid("e0000000-0000-0000-0000-000000000039"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Vijayawada", new Guid("d0000000-0000-0000-0000-000000000001") },
                    { new Guid("e0000000-0000-0000-0000-000000000040"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Visakhapatnam", new Guid("d0000000-0000-0000-0000-000000000001") }
                });

            migrationBuilder.InsertData(
                table: "lookup_items",
                columns: new[] { "id", "category", "code", "is_active", "label", "sort_order" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000001"), "Country", "IN", true, "India", 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000002"), "Country", "US", true, "United States", 2 },
                    { new Guid("c0000000-0000-0000-0000-000000000003"), "Country", "GB", true, "United Kingdom", 3 },
                    { new Guid("c0000000-0000-0000-0000-000000000004"), "Country", "CA", true, "Canada", 4 },
                    { new Guid("c0000000-0000-0000-0000-000000000005"), "Country", "AU", true, "Australia", 5 },
                    { new Guid("c0000000-0000-0000-0000-000000000006"), "Country", "AE", true, "United Arab Emirates", 6 },
                    { new Guid("c0000001-0000-0000-0000-000000000001"), "Gender", "M", true, "Male", 1 },
                    { new Guid("c0000001-0000-0000-0000-000000000002"), "Gender", "F", true, "Female", 2 },
                    { new Guid("c0000001-0000-0000-0000-000000000003"), "Gender", "O", true, "Other", 3 },
                    { new Guid("c0000002-0000-0000-0000-000000000001"), "MaritalStatus", "S", true, "Single", 1 },
                    { new Guid("c0000002-0000-0000-0000-000000000002"), "MaritalStatus", "M", true, "Married", 2 },
                    { new Guid("c0000002-0000-0000-0000-000000000003"), "MaritalStatus", "W", true, "Widowed", 3 },
                    { new Guid("c0000002-0000-0000-0000-000000000004"), "MaritalStatus", "D", true, "Divorced", 4 },
                    { new Guid("c0000003-0000-0000-0000-000000000001"), "Qualification", "B10", true, "Below 10th", 1 },
                    { new Guid("c0000003-0000-0000-0000-000000000002"), "Qualification", "10TH", true, "10th Pass", 2 },
                    { new Guid("c0000003-0000-0000-0000-000000000003"), "Qualification", "12TH", true, "12th Pass", 3 },
                    { new Guid("c0000003-0000-0000-0000-000000000004"), "Qualification", "DIP", true, "Diploma", 4 },
                    { new Guid("c0000003-0000-0000-0000-000000000005"), "Qualification", "GRAD", true, "Graduate", 5 },
                    { new Guid("c0000003-0000-0000-0000-000000000006"), "Qualification", "PG", true, "Post Graduate", 6 },
                    { new Guid("c0000003-0000-0000-0000-000000000007"), "Qualification", "PHD", true, "Doctorate", 7 },
                    { new Guid("c0000004-0000-0000-0000-000000000001"), "ProfessionType", "BUS", true, "Business", 1 },
                    { new Guid("c0000004-0000-0000-0000-000000000002"), "ProfessionType", "SVC", true, "Service / Job", 2 },
                    { new Guid("c0000004-0000-0000-0000-000000000003"), "ProfessionType", "PROF", true, "Professional", 3 },
                    { new Guid("c0000004-0000-0000-0000-000000000004"), "ProfessionType", "STU", true, "Student", 4 },
                    { new Guid("c0000004-0000-0000-0000-000000000005"), "ProfessionType", "HOME", true, "Homemaker", 5 },
                    { new Guid("c0000004-0000-0000-0000-000000000006"), "ProfessionType", "RET", true, "Retired", 6 },
                    { new Guid("c0000004-0000-0000-0000-000000000007"), "ProfessionType", "OTH", true, "Other", 7 },
                    { new Guid("c0000005-0000-0000-0000-000000000001"), "BusinessCategory", "GRO", true, "Grocery", 1 },
                    { new Guid("c0000005-0000-0000-0000-000000000002"), "BusinessCategory", "TEX", true, "Textiles", 2 },
                    { new Guid("c0000005-0000-0000-0000-000000000003"), "BusinessCategory", "JWL", true, "Jewellery", 3 },
                    { new Guid("c0000005-0000-0000-0000-000000000004"), "BusinessCategory", "ELEC", true, "Electronics", 4 },
                    { new Guid("c0000005-0000-0000-0000-000000000005"), "BusinessCategory", "CAT", true, "Catering", 5 },
                    { new Guid("c0000005-0000-0000-0000-000000000006"), "BusinessCategory", "HOSP", true, "Hospitality", 6 },
                    { new Guid("c0000005-0000-0000-0000-000000000007"), "BusinessCategory", "PSVC", true, "Professional Services", 7 },
                    { new Guid("c0000005-0000-0000-0000-000000000008"), "BusinessCategory", "OTH", true, "Other", 8 },
                    { new Guid("c0000006-0000-0000-0000-000000000001"), "EventType", "REL", true, "Religious", 1 },
                    { new Guid("c0000006-0000-0000-0000-000000000002"), "EventType", "CUL", true, "Cultural", 2 },
                    { new Guid("c0000006-0000-0000-0000-000000000003"), "EventType", "SOC", true, "Social", 3 },
                    { new Guid("c0000006-0000-0000-0000-000000000004"), "EventType", "EDU", true, "Educational", 4 },
                    { new Guid("c0000006-0000-0000-0000-000000000005"), "EventType", "CHAR", true, "Charitable", 5 },
                    { new Guid("c0000006-0000-0000-0000-000000000006"), "EventType", "SPRT", true, "Sports", 6 },
                    { new Guid("c0000007-0000-0000-0000-000000000001"), "CompetitionType", "RANG", true, "Rangoli", 1 },
                    { new Guid("c0000007-0000-0000-0000-000000000002"), "CompetitionType", "ELOC", true, "Elocution", 2 },
                    { new Guid("c0000007-0000-0000-0000-000000000003"), "CompetitionType", "QUIZ", true, "Quiz", 3 },
                    { new Guid("c0000007-0000-0000-0000-000000000004"), "CompetitionType", "SING", true, "Singing", 4 },
                    { new Guid("c0000007-0000-0000-0000-000000000005"), "CompetitionType", "DANCE", true, "Dance", 5 },
                    { new Guid("c0000007-0000-0000-0000-000000000006"), "CompetitionType", "ESSAY", true, "Essay Writing", 6 },
                    { new Guid("c0000007-0000-0000-0000-000000000007"), "CompetitionType", "ART", true, "Drawing / Art", 7 }
                });

            migrationBuilder.InsertData(
                table: "states",
                columns: new[] { "id", "country_id", "is_active", "name" },
                values: new object[,]
                {
                    { new Guid("d0000000-0000-0000-0000-000000000001"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Andhra Pradesh" },
                    { new Guid("d0000000-0000-0000-0000-000000000002"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Arunachal Pradesh" },
                    { new Guid("d0000000-0000-0000-0000-000000000003"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Assam" },
                    { new Guid("d0000000-0000-0000-0000-000000000004"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Bihar" },
                    { new Guid("d0000000-0000-0000-0000-000000000005"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Chhattisgarh" },
                    { new Guid("d0000000-0000-0000-0000-000000000006"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Goa" },
                    { new Guid("d0000000-0000-0000-0000-000000000007"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Gujarat" },
                    { new Guid("d0000000-0000-0000-0000-000000000008"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Haryana" },
                    { new Guid("d0000000-0000-0000-0000-000000000009"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Himachal Pradesh" },
                    { new Guid("d0000000-0000-0000-0000-000000000010"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Jharkhand" },
                    { new Guid("d0000000-0000-0000-0000-000000000011"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Karnataka" },
                    { new Guid("d0000000-0000-0000-0000-000000000012"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Kerala" },
                    { new Guid("d0000000-0000-0000-0000-000000000013"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Madhya Pradesh" },
                    { new Guid("d0000000-0000-0000-0000-000000000014"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Maharashtra" },
                    { new Guid("d0000000-0000-0000-0000-000000000015"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Manipur" },
                    { new Guid("d0000000-0000-0000-0000-000000000016"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Meghalaya" },
                    { new Guid("d0000000-0000-0000-0000-000000000017"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Mizoram" },
                    { new Guid("d0000000-0000-0000-0000-000000000018"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Nagaland" },
                    { new Guid("d0000000-0000-0000-0000-000000000019"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Odisha" },
                    { new Guid("d0000000-0000-0000-0000-000000000020"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Punjab" },
                    { new Guid("d0000000-0000-0000-0000-000000000021"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Rajasthan" },
                    { new Guid("d0000000-0000-0000-0000-000000000022"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Sikkim" },
                    { new Guid("d0000000-0000-0000-0000-000000000023"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Tamil Nadu" },
                    { new Guid("d0000000-0000-0000-0000-000000000024"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Telangana" },
                    { new Guid("d0000000-0000-0000-0000-000000000025"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Tripura" },
                    { new Guid("d0000000-0000-0000-0000-000000000026"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Uttar Pradesh" },
                    { new Guid("d0000000-0000-0000-0000-000000000027"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Uttarakhand" },
                    { new Guid("d0000000-0000-0000-0000-000000000028"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "West Bengal" },
                    { new Guid("d0000000-0000-0000-0000-000000000029"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Andaman and Nicobar Islands" },
                    { new Guid("d0000000-0000-0000-0000-000000000030"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Chandigarh" },
                    { new Guid("d0000000-0000-0000-0000-000000000031"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Dadra and Nagar Haveli and Daman and Diu" },
                    { new Guid("d0000000-0000-0000-0000-000000000032"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Delhi" },
                    { new Guid("d0000000-0000-0000-0000-000000000033"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Jammu and Kashmir" },
                    { new Guid("d0000000-0000-0000-0000-000000000034"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Ladakh" },
                    { new Guid("d0000000-0000-0000-0000-000000000035"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Lakshadweep" },
                    { new Guid("d0000000-0000-0000-0000-000000000036"), new Guid("c0000000-0000-0000-0000-000000000001"), true, "Puducherry" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000020"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000024"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000029"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000031"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000032"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000035"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000036"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000038"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000039"));

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000040"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000001-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000001-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000001-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000002-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000002-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000002-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000002-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000004-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000005-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000006-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "lookup_items",
                keyColumn: "id",
                keyValue: new Guid("c0000007-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000020"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000024"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000029"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000031"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000032"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000035"));

            migrationBuilder.DeleteData(
                table: "states",
                keyColumn: "id",
                keyValue: new Guid("d0000000-0000-0000-0000-000000000036"));
        }
    }
}
