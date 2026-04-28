\# APBD Task 6 - Clinic Appointments API



This is my solution for Task 6. It's a REST API for managing clinic appointments, built with ASP.NET Core and ADO.NET. 



As required by the assignment, I did not use Entity Framework. All database communication is handled manually using `SqlConnection`, `SqlCommand`, and `SqlDataReader`. All user inputs are passed as SQL parameters to prevent SQL injection.



\## Tech Stack

\* C#

\* ASP.NET Core Web API

\* ADO.NET (`Microsoft.Data.SqlClient`)

\* SQL Server



\## How to run the project



1\. \*\*Set up the database:\*\* Open SQL Server Management Studio (SSMS) or Azure Data Studio and execute the `01\_create\_and\_seed\_clinic.sql` script. This will create the `ClinicAdoNet` database and fill it with sample data.

&#x20;  

2\. \*\*Check connection string:\*\* Open `appsettings.json` and make sure the `DefaultConnection` string matches your local SQL Server instance name (e.g., `Server=localhost,1433;` or `Server=localhost\\SQLEXPRESS;`).



3\. \*\*Run the API:\*\* Run the project using your IDE (Rider/Visual Studio) or open the terminal and type `dotnet run`. The browser should open the Swagger interface where you can test the endpoints.



\## Endpoints



\* \*\*`GET`\*\* `/api/appointments` - Gets a list of appointments. You can use `?status=` or `?patientLastName=` to filter the results.

\* \*\*`GET`\*\* `/api/appointments/{idAppointment}` - Gets the full details of one specific appointment.

\* \*\*`POST`\*\* `/api/appointments` - Creates a new appointment (validates dates and checks for doctor scheduling conflicts).

\* \*\*`PUT`\*\* `/api/appointments/{idAppointment}` - Updates an existing appointment.

\* \*\*`DELETE`\*\* `/api/appointments/{idAppointment}` - Deletes an appointment (blocks deletion if the status is already 'Completed').

