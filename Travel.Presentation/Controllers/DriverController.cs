using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>The drivers catalogue — the <see cref="TourController"/> shape with nothing but a name and an active flag.</summary>
    [Authorize]
    public class DriverController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<DriverController> _logger;

        public DriverController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<DriverController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.Drivers.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<Driver>();
            spec.SetOrderBy(d => d.Name);
            var drivers = await _unitOfWork.Repository<Driver>().GetAllAsync(spec);
            return View(drivers);
        }

        [Authorize(Policy = Permissions.Drivers.Create)]
        public IActionResult Create() => View(new DriverViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Drivers.Create)]
        public async Task<IActionResult> Create(DriverViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var driver = _mapper.Map<Driver>(model);
            await _unitOfWork.Repository<Driver>().AddAsync(driver);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Driver #{DriverId} created", driver.Id);

            TempData["Success"] = "Driver created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Drivers.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var driver = await LoadAsync(id);
            if (driver is null)
                return NotFound();

            return View(_mapper.Map<DriverViewModel>(driver));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Drivers.Edit)]
        public async Task<IActionResult> Edit(DriverViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var driver = await LoadAsync(model.Id);
            if (driver is null)
                return NotFound();

            _mapper.Map(model, driver);
            _unitOfWork.Repository<Driver>().Update(driver);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Driver #{DriverId} edited", driver.Id);

            TempData["Success"] = "Driver updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Drivers.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var driver = await LoadAsync(id);
            if (driver is null)
            {
                _logger.LogWarning("Delete refused: driver #{DriverId} not found", id);
                return Json(new { success = false, message = "Driver not found." });
            }

            var inUse = await _unitOfWork.Repository<InternalMovement>()
                .GetAllAsync(new BaseSpecifications<InternalMovement>(m => m.DriverId == id));
            if (inUse.Any())
            {
                _logger.LogWarning("Delete refused: driver #{DriverId} is assigned to movements", id);
                return Json(new { success = false, message = "This driver is assigned to recorded movements and can't be deleted. Mark them inactive instead." });
            }

            _unitOfWork.Repository<Driver>().Delete(driver);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Driver #{DriverId} deleted", id);

            return Json(new { success = true, message = "Driver deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Drivers.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<Driver>()
                .GetAllAsync(new BaseSpecifications<Driver>(d => d.Name == name && d.Id != id));

            return Json(!matches.Any());
        }

        private Task<Driver?> LoadAsync(int id) =>
            _unitOfWork.Repository<Driver>().GetByIdAsync(new BaseSpecifications<Driver>(d => d.Id == id))!;
    }
}
