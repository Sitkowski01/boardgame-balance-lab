using Microsoft.EntityFrameworkCore;

namespace BalanceLab.Persistence;

/// <summary>Stan partii w dzienniku.</summary>
public enum MatchStatus
{
    InProgress,

    /// <summary>Partia doszla do stanu koncowego; ma wyniki.</summary>
    Finished,

    /// <summary>Partia przerwana przed koncem (np. limit ruchow); bez wynikow.</summary>
    Abandoned,
}

/// <summary>Naglowek partii: wszystko, czego potrzeba do jej powtorzenia, poza samymi ruchami.</summary>
/// <remarks>Liczby 64-bitowe bez znaku (ziarno, hash) leza w <c>bigint</c> z ta sama sekwencja bitow.</remarks>
internal sealed class MatchRow
{
    public Guid Id { get; set; }

    /// <summary>Klucz gry, np. "klejnoty" — po nim wiadomo, jakimi zasadami odtwarzac.</summary>
    public required string Game { get; set; }

    public string? Variant { get; set; }

    public int PlayerCount { get; set; }

    public long Seed { get; set; }

    public required string[] Players { get; set; }

    public MatchStatus Status { get; set; }

    /// <summary>Liczba ruchow w dzienniku; zarazem znacznik wspolbieznosci przy dopisywaniu.</summary>
    public int Moves { get; set; }

    public long? FinalHash { get; set; }

    public double[]? Scores { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Ruch nr <see cref="Seq"/> (od 1) i hash stanu po nim.</summary>
internal sealed class MoveRow
{
    public Guid MatchId { get; set; }

    public int Seq { get; set; }

    public required string Move { get; set; }

    public long HashAfter { get; set; }
}

/// <summary>Caly stan po ruchu nr <see cref="Seq"/> — z niego powtorka nie musi zaczynac od rozdania.</summary>
internal sealed class SnapshotRow
{
    public Guid MatchId { get; set; }

    public int Seq { get; set; }

    public required string State { get; set; }

    public long Hash { get; set; }
}

internal sealed class JournalDbContext(DbContextOptions<JournalDbContext> options) : DbContext(options)
{
    public DbSet<MatchRow> Matches => Set<MatchRow>();

    public DbSet<MoveRow> Moves => Set<MoveRow>();

    public DbSet<SnapshotRow> Snapshots => Set<SnapshotRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchRow>(match =>
        {
            match.ToTable("matches", t => t.HasCheckConstraint("ck_matches_moves", "moves >= 0"));
            match.Property(m => m.Game).HasMaxLength(64);
            match.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
            match.Property(m => m.Moves).IsConcurrencyToken();
        });

        modelBuilder.Entity<MoveRow>(move =>
        {
            // Klucz (partia, numer): dwa zapisy tego samego ruchu nie przejda, nawet z pominieciem kodu.
            move.ToTable("match_moves", t => t.HasCheckConstraint("ck_match_moves_seq", "seq > 0"));
            move.HasKey(m => new { m.MatchId, m.Seq });
            move.Property(m => m.Move).HasMaxLength(256);
            move.HasOne<MatchRow>().WithMany().HasForeignKey(m => m.MatchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SnapshotRow>(snapshot =>
        {
            snapshot.ToTable("match_snapshots", t => t.HasCheckConstraint("ck_match_snapshots_seq", "seq > 0"));
            snapshot.HasKey(s => new { s.MatchId, s.Seq });
            snapshot.Property(s => s.State).HasColumnType("jsonb");
            snapshot.HasOne<MatchRow>().WithMany().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
