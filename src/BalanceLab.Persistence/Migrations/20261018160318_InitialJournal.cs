using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BalanceLab.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "text", nullable: true),
                    player_count = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    players = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    moves = table.Column<int>(type: "integer", nullable: false),
                    final_hash = table.Column<long>(type: "bigint", nullable: true),
                    scores = table.Column<double[]>(type: "double precision[]", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                    table.CheckConstraint("ck_matches_moves", "moves >= 0");
                });

            migrationBuilder.CreateTable(
                name: "match_moves",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    move = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    hash_after = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_moves", x => new { x.match_id, x.seq });
                    table.CheckConstraint("ck_match_moves_seq", "seq > 0");
                    table.ForeignKey(
                        name: "fk_match_moves_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "match_snapshots",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    hash = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_snapshots", x => new { x.match_id, x.seq });
                    table.CheckConstraint("ck_match_snapshots_seq", "seq > 0");
                    table.ForeignKey(
                        name: "fk_match_snapshots_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Dziennik jest tylko do dopisywania: poprawka historii partii to nowa partia, nie UPDATE.
            // Pilnuje tego baza, wiec obowiazuje takze reczny SQL i przyszly kod, ktory by o tym zapomnial.
            migrationBuilder.Sql("""
                CREATE FUNCTION journal_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Dziennik partii jest tylko do dopisywania (% na %)', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END $$;

                CREATE TRIGGER match_moves_append_only BEFORE UPDATE OR DELETE ON match_moves
                    FOR EACH ROW EXECUTE FUNCTION journal_append_only();
                CREATE TRIGGER match_moves_no_truncate BEFORE TRUNCATE ON match_moves
                    FOR EACH STATEMENT EXECUTE FUNCTION journal_append_only();
                CREATE TRIGGER match_snapshots_append_only BEFORE UPDATE OR DELETE ON match_snapshots
                    FOR EACH ROW EXECUTE FUNCTION journal_append_only();
                CREATE TRIGGER match_snapshots_no_truncate BEFORE TRUNCATE ON match_snapshots
                    FOR EACH STATEMENT EXECUTE FUNCTION journal_append_only();

                -- Naglowek zmienia sie, dopoki partia trwa (licznik ruchow, koniec); zakonczonej juz nie.
                CREATE FUNCTION journal_closed_match() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR OLD.status <> 'InProgress' THEN
                        RAISE EXCEPTION 'Partia % jest zamknieta w dzienniku (% zabroniony)', OLD.id, TG_OP
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER matches_closed BEFORE UPDATE OR DELETE ON matches
                    FOR EACH ROW EXECUTE FUNCTION journal_closed_match();
                CREATE TRIGGER matches_no_truncate BEFORE TRUNCATE ON matches
                    FOR EACH STATEMENT EXECUTE FUNCTION journal_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_moves");

            migrationBuilder.DropTable(
                name: "match_snapshots");

            migrationBuilder.DropTable(
                name: "matches");

            // Triggery znikaja razem z tabelami; funkcje trzeba usunac osobno.
            migrationBuilder.Sql("DROP FUNCTION journal_append_only(); DROP FUNCTION journal_closed_match();");
        }
    }
}
