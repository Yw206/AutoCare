using AutoCare.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AutoCare.Controllers
{
    public class HomeController(IConfiguration configuration) : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", User.IsInRole("Admin") ? "Admin" : "Dashboard");
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Location()
        {
            ViewBag.WorkshopName = configuration["Workshop:Name"] ?? "AutoCare Workshop";
            ViewBag.WorkshopAddress = configuration["Workshop:Address"] ?? "Kuala Lumpur, Malaysia";
            ViewBag.MapQuery = configuration["Workshop:MapQuery"] ?? "Kuala Lumpur, Malaysia";
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
