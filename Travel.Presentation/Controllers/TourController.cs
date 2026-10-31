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
    /// <summary>
    /// The extra-tours catalogue. Deliberately the NationalityController shape with a price and an
    /// active flag added — there is nothing here a booking needs to know about beyond name and rate.
    /// </summary>
    [Authorize]
    public class TourController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<TourController> _logger;

        public TourController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<TourController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.Tours.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<Tour>();
            spec.SetOrderBy(t => t.Name);
            var tours = await _unitOfWork.Repository<Tour>().GetAllAsync(spec);
            return View(tours);
        }

        [Authorize(Policy = Permissions.Tours.Create)]
        public IActionResult Create() => View(new TourViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Tours.Create)]
        public async Task<IActionResult> Create(TourViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var tour = _mapper.Map<Tour>(model);
            await _unitOfWork.Repository<Tour>().AddAsync(tour);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Tour #{TourId} \"{TourName}\" created at {PricePerPerson:0.00} per person",
                tour.Id, tour.Name, tour.PricePerPerson);

            TempData["Success"] = "Tour created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Tours.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var tour = await _unitOfWork.Repository<Tour>()
                .GetByIdAsync(new BaseSpecifications<Tour>(t => t.Id == id));

            if (tour is null)
                return NotFound();

            return View(_mapper.Map<TourViewModel>(tour));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Tours.Edit)]
        public async Task<IActionResult> Edit(TourViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var tour = await _unitOfWork.Repository<Tour>()
                .GetByIdAsync(new BaseSpecifications<Tour>(t => t.Id == model.Id));

            if (tour is null)
                return NotFound();

            // Re-pricing the catalogue never restates a past booking: BookingTour.UnitPrice is a
            // snapshot taken when the tour was added, and nothing here writes to it.
            _mapper.Map(model, tour);
            _unitOfWork.Repository<Tour>().Update(tour);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Tour #{TourId} \"{TourName}\" edited", tour.Id, tour.Name);

            TempData["Success"] = "Tour updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Tours.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var tour = await _unitOfWork.Repository<Tour>()
                .GetByIdAsync(new BaseSpecifications<Tour>(t => t.Id == id));

            if (tour is null)
            {
                _logger.LogWarning("Delete refused: tour #{TourId} not found", id);
                return Json(new { success = false, message = "Tour not found." });
            }

            // The FK is Restrict, so this guard is what turns an in-use delete into a readable
            // message instead of a raw SQL constraint error. Retiring is IsActive = false.
            var inUse = await _unitOfWork.Repository<BookingTour>()
                .GetAllAsync(new BaseSpecifications<BookingTour>(bt => bt.TourId == id));

            if (inUse.Any())
            {
                _logger.LogWarning("Delete refused: tour #{TourId} \"{TourName}\" is sold on bookings", id, tour.Name);
                return Json(new { success = false, message = "This tour is sold on one or more bookings and can't be deleted. Mark it inactive instead." });
            }

            _unitOfWork.Repository<Tour>().Delete(tour);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Tour #{TourId} \"{TourName}\" deleted", id, tour.Name);

            return Json(new { success = true, message = "Tour deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Tours.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<Tour>()
                .GetAllAsync(new BaseSpecifications<Tour>(t => t.Name == name && t.Id != id));

            return Json(!matches.Any());
        }
    }
}
