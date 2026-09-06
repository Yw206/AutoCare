using System.Security.Claims;using AutoCare.Data;using AutoCare.Models;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace AutoCare.Controllers;
[Authorize(Roles="User")]
public class VehiclesController(AppDbContext db):Controller
{
 int Uid=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 public async Task<IActionResult> Index()=>View(await db.Vehicles.Where(x=>x.UserId==Uid).ToListAsync());
 public IActionResult Create()=>View(new Vehicle{Year=DateTime.Today.Year});
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Create(Vehicle m){m.UserId=Uid;if(await db.Vehicles.AnyAsync(x=>x.RegistrationNumber==m.RegistrationNumber))ModelState.AddModelError(nameof(m.RegistrationNumber),"Registration number already exists.");if(!ModelState.IsValid)return View(m);db.Add(m);await db.SaveChangesAsync();TempData["Success"]="Vehicle added.";return RedirectToAction(nameof(Index));}
 public async Task<IActionResult> Edit(int id){var m=await db.Vehicles.FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==Uid);return m==null?NotFound():View(m);}
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Edit(Vehicle m){var v=await db.Vehicles.FirstOrDefaultAsync(x=>x.Id==m.Id&&x.UserId==Uid);if(v==null)return NotFound();ModelState.Remove(nameof(m.UserId));if(!ModelState.IsValid)return View(m);v.RegistrationNumber=m.RegistrationNumber;v.Brand=m.Brand;v.Model=m.Model;v.Year=m.Year;v.Mileage=m.Mileage;v.Colour=m.Colour;await db.SaveChangesAsync();return RedirectToAction(nameof(Index));}
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Delete(int id){var v=await db.Vehicles.FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==Uid);if(v!=null&&!await db.Appointments.AnyAsync(x=>x.VehicleId==id)){db.Remove(v);await db.SaveChangesAsync();}return RedirectToAction(nameof(Index));}
}
