AUTOCARE DATABASE INFORMATION

AutoCare uses SQL Server Express LocalDB, which is a file-based SQL Server
database. SQL Server manages the physical MDF and LDF files to prevent attach
conflicts when the project is extracted or moved.

Database name:
    AutoCareAssignmentDb2026

You can view it in Visual Studio:
    View > SQL Server Object Explorer
    (localdb)\\MSSQLLocalDB > Databases > AutoCareAssignmentDb2026

The tables and assignment demonstration records are created automatically by
Entity Framework Core and DbSeeder when the application first runs.
