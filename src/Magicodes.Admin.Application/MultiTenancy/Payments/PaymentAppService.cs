using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Abp.Authorization;
using Abp.Runtime.Session;
using Abp.UI;
using Magicodes.Admin.Authorization;
using Magicodes.Admin.Editions;
using Magicodes.Admin.Editions.Dto;
using Magicodes.Admin.MultiTenancy.Dto;
using Magicodes.Admin.MultiTenancy.Payments.Cache;
using Magicodes.Admin.MultiTenancy.Payments.Dto;

namespace Magicodes.Admin.MultiTenancy.Payments
{
    public class PaymentAppService : AdminAppServiceBase, IPaymentAppService
    {
        private readonly ISubscriptionPaymentRepository _subscriptionPaymentRepository;
        private readonly EditionManager _editionManager;
        private readonly IPaymentGatewayManagerFactory _paymentGatewayManagerFactory;
        private readonly IPaymentCache _paymentCache;

        public PaymentAppService(
            ISubscriptionPaymentRepository subscriptionPaymentRepository,
            EditionManager editionManager,
            IPaymentGatewayManagerFactory paymentGatewayManagerFactory, IPaymentCache paymentCache)
        {
            _subscriptionPaymentRepository = subscriptionPaymentRepository;
            _editionManager = editionManager;
            _paymentGatewayManagerFactory = paymentGatewayManagerFactory;
            _paymentCache = paymentCache;
        }

        [AbpAuthorize(AppPermissions.Pages_Administration_Tenant_SubscriptionManagement)]
        public async Task<PaymentInfoDto> GetPaymentInfo(PaymentInfoInput input)
        {
            var tenant = await TenantManager.GetByIdAsync(AbpSession.GetTenantId());

            if (tenant.EditionId == null)
            {
                throw new UserFriendlyException(L("TenantEditionIsNotAssigned"));
            }

            var currentEdition = (SubscribableEdition)await _editionManager.GetByIdAsync(tenant.EditionId.Value);
            var targetEdition = input.UpgradeEditionId == null ? currentEdition : (SubscribableEdition)await _editionManager.GetByIdAsync(input.UpgradeEditionId.Value);

            decimal additionalPrice = 0;

            if (input.UpgradeEditionId.HasValue)
            {
                var remainingDaysCount = tenant.CalculateRemainingDayCount();
                if (remainingDaysCount > 0)
                {
                    additionalPrice = TenantManager
                        .GetUpgradePrice(
                            currentEdition,
                            targetEdition,
                            remainingDaysCount
                        );
                }
            }

            var edition = ObjectMapper.Map<EditionSelectDto>(input.UpgradeEditionId == null ? currentEdition : targetEdition);
            await SetAdditionalDataForPaymentGateways(edition);

            return new PaymentInfoDto
            {
                Edition = edition,
                AdditionalPrice = additionalPrice
            };
        }

        private async Task SetAdditionalDataForPaymentGateways(EditionSelectDto edition)
        {
            foreach (var paymentGateway in Enum.GetValues(typeof(SubscriptionPaymentGatewayType)).Cast<SubscriptionPaymentGatewayType>())
            {
                using (var paymentGatewayManager = _paymentGatewayManagerFactory.Create(paymentGateway))
                {
                    var additionalData = await paymentGatewayManager.Object.GetAdditionalPaymentData(ObjectMapper.Map<SubscribableEdition>(edition));
                    edition.AdditionalData.Add(paymentGateway, additionalData);
                }
            }
        }

        public async Task<CreatePaymentResponse> CreatePayment(CreatePaymentDto input)
        {
            var targetEdition = (SubscribableEdition)await _editionManager.GetByIdAsync(input.EditionId);
            var tenant = AbpSession.TenantId == null ? null : await TenantManager.GetByIdAsync(AbpSession.GetTenantId());
            var amount = await CalculateAmountForPaymentAsync(targetEdition, input.PaymentPeriodType, input.EditionPaymentType, tenant);

            using (var paymentGatewayManager = _paymentGatewayManagerFactory.Create(input.SubscriptionPaymentGatewayType))
            {
                var createPaymentResult = await paymentGatewayManager.Object.CreatePaymentAsync(CalculatePaymentDescription(input, targetEdition), amount);

                await _subscriptionPaymentRepository.InsertAsync(
                    new SubscriptionPayment
                    {
                        PaymentPeriodType = input.PaymentPeriodType,
                        EditionId = input.EditionId,
                        TenantId = tenant == null ? 0 : tenant.Id,
                        Gateway = input.SubscriptionPaymentGatewayType,
                        Amount = amount,
                        DayCount = input.PaymentPeriodType.HasValue ? (int)input.PaymentPeriodType.Value : 0,
                        PaymentId = createPaymentResult.GetId(),
                        Status = SubscriptionPaymentStatus.Processing
                    }
                );

                return createPaymentResult;
            }
        }

        public async Task<ExecutePaymentResponse> ExecutePayment(ExecutePaymentDto input)
        {
            using (var paymentGatewayManager = _paymentGatewayManagerFactory.Create(input.Gateway))
            {
                var executePaymentResponse = await paymentGatewayManager.Object.ExecutePaymentAsync(input.AdditionalData);

                await _subscriptionPaymentRepository.UpdateByGatewayAndPaymentIdAsync(
                    input.Gateway,
                    executePaymentResponse.GetId(),
                    AbpSession.TenantId,
                    SubscriptionPaymentStatus.Completed
                );

                var paymentId = executePaymentResponse.GetId();
                _paymentCache.AddCacheItem(new PaymentCacheItem(input.Gateway, input.PaymentPeriodType, paymentId));

                if (AbpSession.TenantId.HasValue)
                {
                    await TenantManager.UpdateTenantAsync(
                        AbpSession.GetTenantId(),
                        true,
                        false,
                        input.PaymentPeriodType,
                        input.EditionId,
                        input.EditionPaymentType
                    );
                }

                return executePaymentResponse;
            }
        }

        private async Task<decimal> CalculateAmountForPaymentAsync(SubscribableEdition targetEdition, PaymentPeriodType? periodType, EditionPaymentType editionPaymentType, Tenant tenant)
        {
            if (editionPaymentType != EditionPaymentType.Upgrade)
            {
                return targetEdition.GetPaymentAmount(periodType);
            }

            if (tenant.EditionId == null)
            {
                throw new UserFriendlyException(L("CanNotUpgradeSubscriptionSinceTenantHasNoEditionAssigned"));
            }

            var remainingDaysCount = tenant.CalculateRemainingDayCount();

            if (remainingDaysCount <= 0)
            {
                return targetEdition.GetPaymentAmount(periodType);
            }

            Debug.Assert(tenant.EditionId != null, "tenant.EditionId != null");

            var currentEdition = (SubscribableEdition)await _editionManager.GetByIdAsync(tenant.EditionId.Value);

            return TenantManager.GetUpgradePrice(currentEdition, targetEdition, remainingDaysCount);
        }

        private string CalculatePaymentDescription(CreatePaymentDto input, SubscribableEdition targetEdition)
        {
            switch (input.EditionPaymentType)
            {
                case EditionPaymentType.NewRegistration:
                case EditionPaymentType.BuyNow:
                    return L("Purchase");
                case EditionPaymentType.Upgrade:
                    return L("UpgradedTo", targetEdition.DisplayName);
                case EditionPaymentType.Extend:
                    return L("ExtendedEdition", targetEdition.DisplayName);
                default:
                    throw new ArgumentException(nameof(input.EditionPaymentType));
            }
        }
    }
}