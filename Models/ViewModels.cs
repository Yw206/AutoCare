using System.ComponentModel.DataAnnotations;

namespace AutoCare.Models;
public class LoginVm { [Required,EmailAddress] public string Email { get; set; }=""; [Required,DataType(DataType.Password)] public string Password { get; set; }=""; }
public class RegisterVm { [Required,StringLength(80)] public string FullName { get; set; }=""; [Required,EmailAddress] public string Email { get; set; }=""; [Required,Phone] public string Phone { get; set; }=""; [Required,StringLength(100,MinimumLength=8),DataType(DataType.Password)] public string Password { get; set; }=""; [Compare(nameof(Password)),DataType(DataType.Password)] public string ConfirmPassword { get; set; }=""; }
public class AppointmentVm { [Required] public int VehicleId { get;set; } [Required] public int WorkshopServiceId { get;set; } [Required,DataType(DataType.DateTime)] public DateTime AppointmentAt { get;set; }=DateTime.Today.AddDays(1).AddHours(9); [Required,StringLength(800,MinimumLength=10)] public string ProblemDescription { get;set; }=""; }
public class VerifyEmailVm { [Required,EmailAddress] public string Email { get;set; }=""; [Required,StringLength(6,MinimumLength=6)] public string Otp { get;set; }=""; }
