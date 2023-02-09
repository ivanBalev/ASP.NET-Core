﻿namespace InSelfLove.Web.Controllers
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using InSelfLove.Data.Models;
    using InSelfLove.Services.Data.Appointments;
    using InSelfLove.Services.Data.Helpers;
    using InSelfLove.Services.Data.Stripe;
    using InSelfLove.Services.Mapping;
    using InSelfLove.Services.Messaging;
    using InSelfLove.Web.Controllers.Helpers;
    using InSelfLove.Web.InputModels.Appointment;
    using InSelfLove.Web.ViewModels.Appointment;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;
    using Stripe;

    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : BaseController
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly IAppointmentService appointmentService;
        private readonly IStripeService stripeService;
        private readonly IEmailSender emailSender;
        private readonly IViewRender viewRender;

        public AppointmentsController(
            IViewRender viewRender,
            IEmailSender emailSender,
            IAppointmentService appointmentService,
            IStripeService stripeService,
            UserManager<ApplicationUser> userManager)
        {
            this.appointmentService = appointmentService;
            this.stripeService = stripeService;
            this.userManager = userManager;
            this.emailSender = emailSender;
            this.viewRender = viewRender;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string timezone)
        {
            // Update user timezone
            var userTimezone = await this.UpdateUserTimezone(timezone);

            // Gather data required by service
            string userId = this.userManager.GetUserId(this.User);
            string adminId = (await this.GetUser(admin: true)).Id;

            // Get appointments
            var appointments = await this.appointmentService.GetAll(userId, adminId, userTimezone);

            // Map db appointments to view model
            var appointmentsViewModel = appointments.Select(a =>
                    AutoMapperConfig.MapperInstance.Map<Appointment, AppointmentViewModel>(a));

            var viewModel = new AppointmentIndexViewModel
            {
                Appointments = appointmentsViewModel,
            };

            return this.View(viewModel);
        }

        [HttpPost]
        [Authorize]
        [Route("Book")]
        public async Task<IActionResult> Book([FromBody] AppointmentInputModel inputModel)
        {
            if (!this.ModelState.IsValid)
            {
                return this.BadRequest();
            }

            // Book appointment in db
            var appointment = await this.appointmentService.Book(
                inputModel.Id, inputModel.Description, inputModel.IsOnSite, this.userManager.GetUserId(this.User));

            // Notify user & admin
            await this.SendEmail(appointment, fromAdmin: false, "NewAppointment");
            await this.SendEmail(appointment, fromAdmin: true, "AwaitingApproval");

            return this.Ok();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("CreatePaymentIntent")]
        public async Task<JsonResult> CreatePaymentIntent([FromBody] int appointmentId)
        {
            // TODO: allow online payment only for confirmed emails;
            var userId = this.userManager.GetUserId(this.User);

            // Return client secret to be attached to our payment form 
            return this.Json(new { clientSecret = await this.stripeService.CreatePaymentIntent(appointmentId, userId) });
        }

        // CSRF check is done in service
        // Standard practice with the Stripe API
        [IgnoreAntiforgeryToken]
        [HttpPost]
        [Route("ConfirmPay")]
        public async Task<IActionResult> ConfirmPay()
        {
            var json = await new StreamReader(this.HttpContext.Request.Body).ReadToEndAsync();
            var stripeSignature = this.Request.Headers["Stripe-Signature"];

            // Provide necessary info to service for validation & confirmation/cancellation of payment
            var paymentResult = await this.stripeService.HandlePayment(json, stripeSignature);

            // Send email only if payment is successful
            if (!string.IsNullOrEmpty(paymentResult.Status) && paymentResult.ObjectId != 0)
            {
                var appointment = await this.appointmentService.GetById(paymentResult.ObjectId);
                await this.SendEmail(appointment, true, paymentResult.Status);
            }

            return this.Ok();
        }

        [HttpPost]
        [Authorize]
        [Route("Cancel")]
        public async Task<ActionResult> Cancel([FromBody] AppointmentManipulateModel input)
        {
            // Gather data for email & service
            var appointment = await this.appointmentService.GetById(input.Id);
            var adminId = (await this.GetUser(admin: true)).Id;
            var userId = this.userManager.GetUserId(this.User);

            // Send cancellation email only if appointment is occupied by registered user,
            // not if admin has occupied the slot themselves after a client has booked via fb/insta/phone
            if (appointment.UserId != null && appointment.UserId != adminId)
            {
                await this.SendEmail(appointment, userId == adminId, "Cancellation");
            }

            await this.appointmentService.Cancel(appointment, userId, adminId);

            return this.Ok();
        }

        // Admin-only methods
        [HttpPost]
        [Route("Create")]
        [Authorize(Roles = AppConstants.AdministratorRoleName)]
        public async Task<IActionResult> Create([FromBody] AvailabilityInputModel availabilityInput)
        {
            if (!this.ModelState.IsValid)
            {
                return this.BadRequest();
            }

            var adminTimezone = await this.GetUserTimezoneId();

            // Send slots and date to service
            await this.appointmentService.Create(availabilityInput.TimeSlots, availabilityInput.Date, adminTimezone);
            return this.Ok();
        }

        [HttpPost]
        [Authorize(Roles = AppConstants.AdministratorRoleName)]
        [Route("Approve")]
        public async Task<ActionResult> Approve([FromBody] AppointmentManipulateModel input)
        {
            // Approve appointment in db
            var appointment = await this.appointmentService.Approve(input.Id);

            // Notify user
            await this.SendEmail(appointment, fromAdmin: true, "Confirmation");

            return this.Ok();
        }

        [HttpPost]
        [Authorize(Roles = AppConstants.AdministratorRoleName)]
        [Route("Occupy")]
        public async Task<ActionResult> Occupy([FromBody] AppointmentManipulateModel input)
        {
            // Mark available appointment as occupied by admin.
            // Used for displaying appointments made via fb/insta/phone.
            await this.appointmentService.Occupy(input.Id, this.userManager.GetUserId(this.User));
            return this.Ok();
        }

        [HttpPost]
        [Authorize(Roles = AppConstants.AdministratorRoleName)]
        [Route("SetOnSite")]
        public async Task<ActionResult> SetOnSite([FromBody] AppointmentManipulateModel input)
        {
            // Mark if appointment can be on site
            await this.appointmentService.SetOnSite(input.Id, input.CanBeOnSite);

            return this.Ok();
        }

        // Helper methods
        private async Task<string> UpdateUserTimezone(string timezoneFromQuery)
        {
            var user = await this.GetUser();
            string userCurrentTimezone = this.UserTimezoneIdFromCookie ?? timezoneFromQuery;

            // If no data is sent by client, there's nothing to update with
            if (userCurrentTimezone == null)
            {
                // Try and return what's stored in db or return nothing
                return user?.WindowsTimezoneId ?? null;
            }

            // Convert user's current timezone to windows timezone
            string userCurrentWindowsTimezoneId = TimezoneHelper.GetTimezone(userCurrentTimezone).Id;

            // Compare current timezone from cookie/query param with stored timezone in db
            if (user != null &&
                user.WindowsTimezoneId.ToLower().CompareTo(userCurrentWindowsTimezoneId.ToLower()) != 0)
            {
                // Update if current & stored timezones don't match
                user.WindowsTimezoneId = userCurrentWindowsTimezoneId;
                await this.userManager.UpdateAsync(user);
            }

            // Return current timezone
            return userCurrentTimezone;
        }

        private async Task<string> GetUserTimezoneId(string timezoneFromQuery = null, ApplicationUser user = null)
        {
            if (this.UserTimezoneIdFromCookie == null)
            {
                // Get timezone from db or query if user hasn't given cookie consent
                user = user ?? await this.userManager.GetUserAsync(this.User);
                return user?.WindowsTimezoneId ?? timezoneFromQuery;
            }

            return this.UserTimezoneIdFromCookie;
        }

        private async Task SendEmail(Appointment apptmnt, bool fromAdmin, string status)
        {
            // Get admin
            var admin = await this.GetUser(true);

            // Get user
            var user = apptmnt.User ?? await this.GetUser();

            // Get current user timezone
            var recipientTimezoneId = fromAdmin ? await this.GetUserTimezoneId(string.Empty, user) : admin.WindowsTimezoneId;

            // Define data for email
            var model = new AppointmentEmail()
            {
                Start = TimezoneHelper.ToLocalTime(apptmnt.UtcStart, recipientTimezoneId),
                Status = status,
                Description = apptmnt.Description,
                IsOnSite = apptmnt.IsOnSite,
            };

            // Compose email body
            var emailBody = await this.viewRender.RenderPartialViewToString("_EmailBody", model);

            // Send email
            await this.emailSender.SendEmailAsync(
                from: fromAdmin ? admin.Email : user.Email,
                fromName: fromAdmin ? AppConstants.SystemName : user.UserName,
                to: fromAdmin ? user.Email : admin.Email,
                subject: "Терапевтична сесия",
                htmlContent: emailBody);
        }

        private async Task<ApplicationUser> GetUser(bool admin = false)
        {
            return admin ?
               (await this.userManager.GetUsersInRoleAsync(AppConstants.AdministratorRoleName))
               .FirstOrDefault() :
                await this.userManager.GetUserAsync(this.User);
        }
    }
}
