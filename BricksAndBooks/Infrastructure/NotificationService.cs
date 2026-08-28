using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Infrastructure
{
    public class NotificationService
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationService(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
        }

        /// <summary>
        /// Creates a payment successful notification for a user.
        /// </summary>
        /// <param name="userId">The ID of the user to notify</param>
        /// <param name="paymentId">The ID of the payment that was successful</param>
        /// <param name="amount">The amount of the payment</param>
        public void CreatePaymentSuccessfulNotification(string userId, int paymentId, decimal amount)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.PaymentSuccessful,
                Title = "Payment Successful",
                Message = $"Your payment of {amount:C} was successfully processed.",
                RelatedEntityId = paymentId,
                Url = $"/Payment/Details/{paymentId}"
            };

            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a payment pending-verification notification for a user.
        /// </summary>
        public void CreatePaymentPendingNotification(string userId, int paymentId, decimal amount)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.PaymentPending,
                Title = "Payment Submitted",
                Message = $"Your payment of {amount:C} was recorded and is awaiting verification.",
                RelatedEntityId = paymentId,
                Url = $"/Payment/Details/{paymentId}"
            };
            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a payment failed notification for a user.
        /// </summary>
        /// <param name="userId">The ID of the user to notify</param>
        /// <param name="paymentId">The ID of the payment that failed</param>
        /// <param name="amount">The amount of the payment</param>
        public void CreatePaymentFailedNotification(string userId, int paymentId, decimal amount)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.PaymentFailed,
                Title = "Payment Failed",
                Message = $"Your payment of {amount:C} failed to process. Please try again.",
                RelatedEntityId = paymentId,
                Url = $"/Payment/Details/{paymentId}"
            };

            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a rent due notification for a user.
        /// </summary>
        /// <param name="userId">The ID of the user to notify</param>
        /// <param name="billId">The ID of the bill that is due</param>
        /// <param name="amount">The amount due</param>
        /// <param name="dueDate">The due date of the bill</param>
        public void CreateRentDueNotification(string userId, int billId, decimal amount, DateTime dueDate)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.RentDue,
                Title = "Rent Due",
                Message = $"Your rent payment of {amount:C} is due on {dueDate:dd MMM yyyy}.",
                RelatedEntityId = billId,
                Url = $"/Payment/Process?billId={billId}"
            };

            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a maintenance update notification for a user.
        /// </summary>
        /// <param name="userId">The ID of the user to notify</param>
        /// <param name="maintenanceRequestId">The ID of the maintenance request that was updated</param>
        /// <param name="status">The new status of the maintenance request</param>
        public void CreateMaintenanceUpdateNotification(string userId, int maintenanceRequestId, string status)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.MaintenanceUpdate,
                Title = "Maintenance Request Update",
                Message = $"Your maintenance request has been updated to status: {status}.",
                RelatedEntityId = maintenanceRequestId,
                Url = $"/Maintenance/Details/{maintenanceRequestId}"
            };

            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a new message notification for a user.
        /// </summary>
        /// <param name="userId">The ID of the user to notify (recipient)</param>
        /// <param name="chatMessageId">The ID of the chat message</param>
        /// <param name="senderName">The name of the sender</param>
        public void CreateNewMessageNotification(string userId, int chatMessageId, string senderName)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.NewMessage,
                Title = "New Message",
                Message = $"You have a new message from {senderName}.",
                RelatedEntityId = chatMessageId,
                Url = $"/Chat/Details/{chatMessageId}"
            };

            _repositoryWrapper.Notification.Create(notification);
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates a notification after a user or an authorised officer completes an activity.
        /// </summary>
        public void CreateActivityNotification(string userId, string title, string message, string? url = null, int? relatedEntityId = null)
        {
            _repositoryWrapper.Notification.Create(new Notification
            {
                UserId = userId,
                Type = NotificationType.Activity,
                Title = title,
                Message = message,
                Url = url,
                RelatedEntityId = relatedEntityId
            });
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates an activity notification for every user assigned to a role.
        /// </summary>
        public void CreateRoleActivityNotification(string roleName, string title, string message, string? url = null, int? relatedEntityId = null)
        {
            var recipients = _userManager.GetUsersInRoleAsync(roleName).GetAwaiter().GetResult();
            foreach (var recipient in recipients)
            {
                _repositoryWrapper.Notification.Create(new Notification
                {
                    UserId = recipient.Id,
                    Type = NotificationType.Activity,
                    Title = title,
                    Message = message,
                    Url = url,
                    RelatedEntityId = relatedEntityId
                });
            }
            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Creates an announcement notification for all users in a specific role.
        /// </summary>
        /// <param name="roleName">The role to send the announcement to (e.g., "Admin", "FinancialOfficer")</param>
        /// <param name="title">The title of the announcement</param>
        /// <param name="message">The message of the announcement</param>
        /// <param name="url">Optional URL for more details</param>
        public void CreateAnnouncementNotification(string roleName, string title, string message, string? url = null)
        {
            var recipients = _userManager.GetUsersInRoleAsync(roleName).GetAwaiter().GetResult();
            foreach (var recipient in recipients)
            {
                _repositoryWrapper.Notification.Create(new Notification
                {
                    UserId = recipient.Id,
                    Type = NotificationType.Announcement,
                    Title = title,
                    Message = message,
                    Url = url
                });
            }
            _repositoryWrapper.Save();
        }
    }
}