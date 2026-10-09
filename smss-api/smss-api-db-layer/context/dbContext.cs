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
        public DbSet<LutStudentCategory> LutStudentCategories { get; set; } = null!;
        public DbSet<LutReligionCategory> LutReligionCategories { get; set; } = null!;
        public DbSet<LutBloodGroup> LutBloodGroups { get; set; } = null!;
        public DbSet<LutGender> LutGenders { get; set; } = null!;
        public DbSet<LutRole> LutRoles { get; set; } = null!;
        public DbSet<TbSchools> Schools { get; set; } = null!;
        public DbSet<TbUsers> Users { get; set; } = null!; 
        public DbSet<TbUserRoles> UserRoles { get; set; } = null!;
        public DbSet<TbRefreshTokens> RefreshTokens { get; set; } = null!;
        public DbSet<TbSchoolContacts> SchoolContacts { get; set; } = null!;
        public DbSet<TbAcademicYears> AcademicYears { get; set; } = null!;
        public DbSet<TbClasses> Classes { get; set; } = null!;
        public DbSet<TbSections> Sections { get; set; } = null!;
        public DbSet<TbClassSubjects> ClassSubjects { get; set; } = null!;
        public DbSet<TbDocumentType> DocumentTypes { get; set; } = null!;
        public DbSet<TbSubjects> Subjects { get; set; } = null!;
        public DbSet<TbEmployeeDesignations> EmployeeDesignations { get; set; } = null!;
        public DbSet<TbEmployeeDepartments> EmployeeDepartments { get; set; } = null!;
        public DbSet<TbAdmissionTypes> AdmissionTypes { get; set; } = null!;

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

            // TB SCHOOLS
            modelBuilder.Entity<TbSchools>(e =>
            {
                e.ToTable("tb_schools");
                e.HasKey(x => x.SchoolId);

                e.HasIndex(x => x.SchoolCode).IsUnique();
                e.HasIndex(x => x.SchoolGstin).IsUnique();
                e.HasIndex(x => x.SchoolPan).IsUnique();
                e.HasMany(x => x.Contacts)
                 .WithOne()
                 .HasForeignKey(c => c.SchoolId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TbUsers>(e =>
            {
                e.ToTable("tb_users");
                e.HasKey(x => x.UserId);
                e.Property(x => x.UserId).UseIdentityAlwaysColumn();
                e.HasIndex(x => x.Username).IsUnique();


                e.HasOne<TbSchools>().WithMany().HasForeignKey(x => x.SchoolId);
            });

            modelBuilder.Entity<TbRefreshTokens>(e =>
            {
                e.ToTable("tb_refresh_tokens");
                e.HasKey(x => x.RefreshTokenId);
                e.Property(x => x.RefreshTokenId).UseIdentityAlwaysColumn();
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasIndex(x => x.UserId);

                e.HasOne<TbUsers>()
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TbUserRoles>(e =>
            {
                e.ToTable("tb_user_roles");
                e.HasKey(x => x.UserRoleId);
                e.Property(x => x.UserRoleId).UseIdentityAlwaysColumn();
                e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();

                e.HasOne(x => x.User)
                 .WithMany(u => u.UserRoles)
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne<LutRole>()
                 .WithMany()
                 .HasForeignKey(x => x.RoleId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // inside the existing modelBuilder.Entity<TbSchools>(e => { ... }) block, add:
            

            // new block
            modelBuilder.Entity<TbSchoolContacts>(e =>
            {
                e.ToTable("tb_school_contacts");
                e.HasKey(x => x.ContactId);
                e.Property(x => x.ContactId).UseIdentityAlwaysColumn();
            });
        }

    }
}
