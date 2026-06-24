using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using ZKTECO_LIVE20R.Models;

namespace ZKTECO_LIVE20R.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees { get; set; }
    public DbSet<FingerPrint> FingerPrints { get; set; }
    public DbSet<AttendanceLog> AttendanceLogs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string? connectionString = configuration.GetConnectionString("PostgresConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'PostgresConnection' not found.");

            }
            else
            {
                optionsBuilder.UseNpgsql(connectionString);

            }
        }

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Position).HasMaxLength(100);
        });

        modelBuilder.Entity<FingerPrint>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.TemplateData).IsRequired();

            entity.HasOne(f => f.Employee)
                .WithMany(e => e.FingerPrints)
                .HasForeignKey(f => f.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.HasOne(a => a.Employee)
            .WithMany(e => e.AttendanceLogs)
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });


    }
}
