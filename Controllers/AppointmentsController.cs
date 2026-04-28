using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException();
        }

        [HttpGet]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? patientLastName)
        {
            const string sql = """
                SELECT 
                    a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, 
                    p.FirstName + N' ' + p.LastName AS PatientFullName, 
                    p.Email AS PatientEmail
                FROM dbo.Appointments a
                JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                WHERE (@Status IS NULL OR a.Status = @Status)
                  AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                ORDER BY a.AppointmentDate;
                """;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            
            command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

            await connection.OpenAsync();
            var results = new List<AppointmentListDto>();
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                results.Add(new AppointmentListDto
                {
                    IdAppointment = reader.GetInt32(0),
                    AppointmentDate = reader.GetDateTime(1),
                    Status = reader.GetString(2),
                    Reason = reader.GetString(3),
                    PatientFullName = reader.GetString(4),
                    PatientEmail = reader.GetString(5)
                });
            }
            return Ok(results);
        }

        [HttpGet("{idAppointment}", Name = "GetAppointment")]
        public async Task<IActionResult> GetAppointment(int idAppointment)
        {
            const string sql = """
                SELECT 
                    a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                    p.FirstName + N' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhone,
                    d.FirstName + N' ' + d.LastName AS DoctorFullName, d.LicenseNumber AS DoctorLicense
                FROM dbo.Appointments a
                JOIN dbo.Patients p ON a.IdPatient = p.IdPatient
                JOIN dbo.Doctors d ON a.IdDoctor = d.IdDoctor
                WHERE a.IdAppointment = @IdAppointment;
                """;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return NotFound(new ErrorResponseDto("Appointment not found."));
            }

            var result = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                InternalNotes = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = reader.GetDateTime(5),
                PatientFullName = reader.GetString(6),
                PatientEmail = reader.GetString(7),
                PatientPhone = reader.GetString(8),
                DoctorFullName = reader.GetString(9),
                DoctorLicense = reader.GetString(10)
            };
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
        {
            if (request.AppointmentDate < DateTime.UtcNow)
                return BadRequest(new ErrorResponseDto("Appointment date cannot be in the past."));
            
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
                return BadRequest(new ErrorResponseDto("Reason is required and must be at most 250 characters."));

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string checkEntitiesSql = """
                SELECT (SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient) AS PatientActive,
                       (SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor) AS DoctorActive;
                """;
            await using var checkEntitiesCmd = new SqlCommand(checkEntitiesSql, connection);
            checkEntitiesCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            checkEntitiesCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);

            await using (var reader = await checkEntitiesCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                if (reader.IsDBNull(0)) return NotFound(new ErrorResponseDto("Patient not found."));
                if (!reader.GetBoolean(0)) return BadRequest(new ErrorResponseDto("Patient is not active."));
                if (reader.IsDBNull(1)) return NotFound(new ErrorResponseDto("Doctor not found."));
                if (!reader.GetBoolean(1)) return BadRequest(new ErrorResponseDto("Doctor is not active."));
            }

            const string checkConflictSql = "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate;";
            await using var checkConflictCmd = new SqlCommand(checkConflictSql, connection);
            checkConflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            checkConflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            if ((int)(await checkConflictCmd.ExecuteScalarAsync() ?? 0) > 0)
                return Conflict(new ErrorResponseDto("Doctor already has an appointment at this time."));

            int newId;
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                const string insertSql = """
                    INSERT INTO dbo.Appointments (IdDoctor, IdPatient, AppointmentDate, Reason, Status)
                    OUTPUT INSERTED.IdAppointment
                    VALUES (@IdDoctor, @IdPatient, @AppointmentDate, @Reason, 'Scheduled');
                    """;

                await using var command = new SqlCommand(insertSql, connection, transaction);
                command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                command.Parameters.AddWithValue("@Reason", request.Reason);

                newId = (int)(await command.ExecuteScalarAsync())!;
                await transaction.CommitAsync();
            }

            return CreatedAtRoute("GetAppointment", new { idAppointment = newId }, null);
        }

        [HttpPut("{idAppointment}")]
        public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
        {
            var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
            if (!validStatuses.Contains(request.Status))
                return BadRequest(new ErrorResponseDto("Invalid status. Allowed values: Scheduled, Completed, Cancelled."));

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string getAppSql = "SELECT AppointmentDate, Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";
            await using var getAppCmd = new SqlCommand(getAppSql, connection);
            getAppCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
            
            DateTime currentAppointmentDate;
            string currentStatus;

            await using (var reader = await getAppCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return NotFound(new ErrorResponseDto("Appointment not found."));
                currentAppointmentDate = reader.GetDateTime(0);
                currentStatus = reader.GetString(1);
            }

            if (currentStatus == "Completed" && currentAppointmentDate != request.AppointmentDate)
                return BadRequest(new ErrorResponseDto("Cannot change the date of a completed appointment."));

            const string checkEntitiesSql = """
                SELECT (SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient) AS PatientActive,
                       (SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor) AS DoctorActive;
                """;
            await using var checkEntitiesCmd = new SqlCommand(checkEntitiesSql, connection);
            checkEntitiesCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            checkEntitiesCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);

            await using (var reader = await checkEntitiesCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                if (reader.IsDBNull(0)) return NotFound(new ErrorResponseDto("Patient not found."));
                if (!reader.GetBoolean(0)) return BadRequest(new ErrorResponseDto("Patient is not active."));
                if (reader.IsDBNull(1)) return NotFound(new ErrorResponseDto("Doctor not found."));
                if (!reader.GetBoolean(1)) return BadRequest(new ErrorResponseDto("Doctor is not active."));
            }

            if (currentAppointmentDate != request.AppointmentDate)
            {
                const string checkConflictSql = "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate AND IdAppointment != @IdAppointment;";
                await using var checkConflictCmd = new SqlCommand(checkConflictSql, connection);
                checkConflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                checkConflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                checkConflictCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
                if ((int)(await checkConflictCmd.ExecuteScalarAsync() ?? 0) > 0)
                    return Conflict(new ErrorResponseDto("Doctor already has an appointment at this time."));
            }

            const string updateSql = """
                UPDATE dbo.Appointments
                SET IdPatient = @IdPatient, IdDoctor = @IdDoctor, AppointmentDate = @AppointmentDate,
                    Status = @Status, Reason = @Reason, InternalNotes = @InternalNotes
                WHERE IdAppointment = @IdAppointment;
                """;
            await using var updateCmd = new SqlCommand(updateSql, connection);
            updateCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            updateCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            updateCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            updateCmd.Parameters.AddWithValue("@Status", request.Status);
            updateCmd.Parameters.AddWithValue("@Reason", request.Reason);
            updateCmd.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await updateCmd.ExecuteNonQueryAsync();
            return Ok(request);
        }

        [HttpDelete("{idAppointment}")]
        public async Task<IActionResult> DeleteAppointment(int idAppointment)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string getStatusSql = "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";
            await using var getStatusCmd = new SqlCommand(getStatusSql, connection);
            getStatusCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);

            var status = await getStatusCmd.ExecuteScalarAsync() as string;

            if (status == null) return NotFound(new ErrorResponseDto("Appointment not found."));
            if (status == "Completed") return Conflict(new ErrorResponseDto("Cannot delete a completed appointment."));

            const string deleteSql = "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";
            await using var deleteCmd = new SqlCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await deleteCmd.ExecuteNonQueryAsync();
            return NoContent();
        }
    }
}