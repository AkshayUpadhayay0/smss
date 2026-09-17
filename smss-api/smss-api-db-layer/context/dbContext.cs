using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.entity;


namespace smss_api_db_layer.context
{
    public class dbContext : DbContext
    {
        public dbContext() { }

        public dbContext(DbContextOptions<dbContext> options) : base(options)
        {
        }

        // ========================================================= // LUT TABLES // =========================================================
        public DbSet<LutCountry> LutCountries { get; set; } = null!; 
        public DbSet<LutState> LutStates { get; set; } = null!; 
        public DbSet<LutDistrict> LutDistricts { get; set; } = null!; 
        public DbSet<LutCity> LutCities { get; set; } = null!; 
        public DbSet<LutStatus> LutStatus { get; set; } = null!;

        public DbSet<LutBoardType> LutBoardTypes { get; set; } = null!;

        public DbSet<LutSchoolType> LutSchoolTypes { get; set; } = null!;

        public DbSet<LutSchoolLevel> LutSchoolLevels { get; set; } = null!;
        public DbSet<LutRole> LutRoles { get; set; } = null!;

        // ========================================================= // MODEL CONFIGURATION // =========================================================
        protected override void OnModelCreating(ModelBuilder modelBuilder) 
        { 
            base.OnModelCreating(modelBuilder); 
        
            // ===================================================== // LUT COUNTRY // =====================================================
            modelBuilder.Entity<LutCountry>() .HasKey(x => x.Cid); 
            
            // ===================================================== // LUT STATE // Composite Primary Key: // (cid, sid) // =====================================================
            modelBuilder.Entity<LutState>() .HasKey(x => new { x.Cid, x.Sid }); 
            
            // ===================================================== // LUT DISTRICT // Composite Primary Key: // (cid, sid, did) // =====================================================
            modelBuilder.Entity<LutDistrict>() .HasKey(x => new { x.Cid, x.Sid, x.Did }); 
            
            // ===================================================== // LUT CITY // Composite Primary Key: // (cid, sid, did, city_id) // =====================================================
            modelBuilder.Entity<LutCity>() .HasKey(x => new { x.Cid, x.Sid, x.Did, x.CityId });

            // ===================================================== // LUT Status // =====================================================
            modelBuilder.Entity<LutStatus>().HasKey(x => x.Sid);

            // =====================================================
            // LUT BOARD TYPE
            // =====================================================

            modelBuilder.Entity<LutBoardType>()
                .HasKey(x => x.BoardTypeId);


            // =====================================================
            // LUT SCHOOL TYPE
            // =====================================================

            modelBuilder.Entity<LutSchoolType>()
                .HasKey(x => x.SchoolTypeId);


            // =====================================================
            // LUT SCHOOL LEVEL
            // =====================================================

            modelBuilder.Entity<LutSchoolLevel>()
                .HasKey(x => x.SchoolLevelId);

            // =====================================================
            // LUT ROLES
            // =====================================================

            modelBuilder.Entity<LutRole>()
                .HasKey(x => x.RoleId);

        }

    }
}
