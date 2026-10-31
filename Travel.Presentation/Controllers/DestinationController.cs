using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Authorization;
using Travel.Domain.Specifications;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class DestinationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<DestinationController> _logger;

        public DestinationController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<DestinationController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.Destinations.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<Destination>();
            spec.SetOrderBy(d => d.Name);
            var destinations = await _unitOfWork.Repository<Destination>().GetAllAsync(spec);
            return View(destinations);
        }

        [Authorize(Policy = Permissions.Destinations.Create)]
        public IActionResult Create() => View(new DestinationViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Destinations.Create)]
        public async Task<IActionResult> Create(DestinationViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var destination = _mapper.Map<Destination>(model);
            await _unitOfWork.Repository<Destination>().AddAsync(destination);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Destination #{DestinationId} \"{DestinationName}\" created", destination.Id, destination.Name);

            TempData["Success"] = "Destination created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Destinations.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var destination = await _unitOfWork.Repository<Destination>()
                .GetByIdAsync(new BaseSpecifications<Destination>(d => d.Id == id));

            if (destination is null)
                return NotFound();

            return View(_mapper.Map<DestinationViewModel>(destination));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Destinations.Edit)]
        public async Task<IActionResult> Edit(DestinationViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var destination = await _unitOfWork.Repository<Destination>()
                .GetByIdAsync(new BaseSpecifications<Destination>(d => d.Id == model.Id));

            if (destination is null)
                return NotFound();

            _mapper.Map(model, destination);
            _unitOfWork.Repository<Destination>().Update(destination);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Destination #{DestinationId} \"{DestinationName}\" edited", destination.Id, destination.Name);

            TempData["Success"] = "Destination updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Destinations.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var destination = await _unitOfWork.Repository<Destination>()
                .GetByIdAsync(new BaseSpecifications<Destination>(d => d.Id == id));

            if (destination is null)
            {
                _logger.LogWarning("Delete refused: destination #{DestinationId} not found", id);
                return Json(new { success = false, message = "Destination not found." });
            }

            var usedAsBase = await _unitOfWork.Repository<Package>()
                .GetAllAsync(new BaseSpecifications<Package>(p => p.BaseDestinationId == id));
            var usedInSegment = await _unitOfWork.Repository<PackageSegment>()
                .GetAllAsync(new BaseSpecifications<PackageSegment>(s => s.DestinationId == id));

            if (usedAsBase.Any() || usedInSegment.Any())
            {
                _logger.LogWarning("Delete refused: destination #{DestinationId} \"{DestinationName}\" is used by packages", id, destination.Name);
                return Json(new { success = false, message = "This destination is used by one or more packages and can't be deleted." });
            }

            _unitOfWork.Repository<Destination>().Delete(destination);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Destination #{DestinationId} \"{DestinationName}\" deleted", id, destination.Name);

            return Json(new { success = true, message = "Destination deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Destinations.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<Destination>()
                .GetAllAsync(new BaseSpecifications<Destination>(d => d.Name == name && d.Id != id));

            return Json(!matches.Any());
        }
    }
}
