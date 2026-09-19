# API reference

Every HTTP endpoint the Prepaid Engine API exposes: method, path, who may call it, and its parameters.
Generated from the running app (123 endpoints), so it matches the code. Do not edit by hand; regenerate with:

```
ASPNETCORE_ENVIRONMENT=Production Jwt__Key=<any 32+ characters> dotnet run --project backend/PrepaidEngine.Api -- dump-endpoints docs/API_REFERENCE.md
```

## Conventions

- Base path `/api/v1` (except `/health`). JSON in and out. Send `Authorization: Bearer <token>` from `POST /api/v1/auth/login`.
- Optionally send `X-Correlation-Id` (8 to 64 letters, digits or hyphens); it is echoed back and stored on audit entries.
- Enums are sent as numbers unless a parameter is described as a string. Dates are ISO 8601 (`yyyy-MM-dd` or full UTC timestamps).
- Paged searches return `{ items, nextCursor, totalCount }`; pass `nextCursor` back as `after`. `pageSize` is capped at 100.
- Unpaged lists return at most 1,000 rows and set `X-Result-Truncated: true` when more exist. Reports return at most 5,000 rows and say so in `truncated`.
- Errors: `400` validation, `401` missing or invalid token, `403` your role may not do this, `404` not found, `409` conflict, `429` rate limit (`Retry-After` is set), `503` an upstream adapter is down.

## Access policies

| Policy | Who may call |
|---|---|
| Any signed-in user | every role, including `ReadOnly` (all reads use this) |
| `Authenticated` | any signed-in user |
| `DataAdmin` | Admin, IT |
| `ITRole` | Admin, IT |
| `Operations` | Admin, IT, Operator |
| `TariffGovernanceRole` | Admin, IT, Utility |
| `UtilityRole` | Utility |

Roles: `Admin`, `IT`, `Operator`, `Utility`, `ReadOnly`. See [assumptions-and-security.md](assumptions-and-security.md) for what each may do.

## Analytics

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/analytics/balance-history` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional) |
| GET | `/api/v1/analytics/overview` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional) |

## Audit entries

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/audit-entries` | Any signed-in user | query `entityType` string (optional)<br>query `entityId` string (optional)<br>query `from` date (optional)<br>query `to` date (optional) |
| GET | `/api/v1/audit-entries/search` | Any signed-in user | query `q` string (optional)<br>query `entityType` string (optional)<br>query `actor` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/audit-entries/summary` | Any signed-in user | none |

## Authentication

| Method | Path | Access | Parameters |
|---|---|---|---|
| POST | `/api/v1/auth/forgot-password` | Anonymous | **body** `ForgotPasswordRequest` { `username` string? } |
| POST | `/api/v1/auth/login` | Anonymous | **body** `LoginRequest` { `username` string?, `password` string? } |
| POST | `/api/v1/auth/logout` | `Authenticated` (any signed-in user) | none |
| POST | `/api/v1/auth/refresh` | `Authenticated` (any signed-in user) | none |
| POST | `/api/v1/auth/reset-password` | Anonymous | **body** `ResetPasswordRequest` { `username` string?, `code` string?, `newPassword` string? } |
| GET | `/api/v1/auth/whoami` | Any signed-in user | none |

## Billing

| Method | Path | Access | Parameters |
|---|---|---|---|
| POST | `/api/v1/billing/daily/{billingDate}/stage1` | `DataAdmin` (Admin, IT) | path `billingDate` date<br>query `cutoff` date (optional) |
| POST | `/api/v1/billing/daily/{billingDate}/stage2` | `DataAdmin` (Admin, IT) | path `billingDate` date<br>query `cutoff` date (optional) |

## Billing reconciliation

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/billing-reconciliation/daily-export` | Any signed-in user | query `date` date |

## Bills

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/bills` | Any signed-in user | none |
| GET | `/api/v1/bills/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/bills/search` | Any signed-in user | query `q` string (optional)<br>query `status` BillStatus (Generated|Paid|PartiallyPaid|Overdue|Cancelled) (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/bills/summary` | Any signed-in user | none |

## Calculation workbench

| Method | Path | Access | Parameters |
|---|---|---|---|
| POST | `/api/v1/calculation-workbench/simulate` | `Authenticated` (any signed-in user) | **body** `SimulateChargeRequest` { `tariffId` guid, `consumptionKwh` number, `connectedLoadOrContractDemand` number } |

## Connectivity commands (RC/DC)

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/connectivity-commands` | Any signed-in user | query `accountNumber` string (optional) |
| GET | `/api/v1/connectivity-commands/{id:guid}` | Any signed-in user | path `id` guid |
| POST | `/api/v1/connectivity-commands/{id:guid}/retry` | `Operations` (Admin, IT, Operator) | path `id` guid |
| GET | `/api/v1/connectivity-commands/search` | Any signed-in user | query `q` string (optional)<br>query `type` ConnectivityCommandType (Disconnect|Reconnect) (optional)<br>query `status` string (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/connectivity-commands/summary` | Any signed-in user | none |

## Consumers

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/consumers` | Any signed-in user | none |
| GET | `/api/v1/consumers/{accountNumber}` | Any signed-in user | path `accountNumber` string |
| POST | `/api/v1/consumers/{accountNumber}/disconnect` | `Operations` (Admin, IT, Operator) | path `accountNumber` string<br>**body** `ConnectivityRequest` { `reason` string, `correlationId` string? } |
| PUT | `/api/v1/consumers/{accountNumber}/mobile` | `Operations` (Admin, IT, Operator) | path `accountNumber` string<br>**body** `UpdateMobileRequest` { `mobileNumber` string? } |
| POST | `/api/v1/consumers/{accountNumber}/recharge` | `Operations` (Admin, IT, Operator) | path `accountNumber` string<br>**body** `RechargeRequest` { `amount` number, `idempotencyKey` string? } |
| POST | `/api/v1/consumers/{accountNumber}/reconciliation-adjustments` | `Operations` (Admin, IT, Operator) | path `accountNumber` string<br>**body** `ReconciliationAdjustmentRequest` { `amount` number, `reconciliationDate` date, `reference` string } |
| POST | `/api/v1/consumers/{accountNumber}/reconnect` | `Operations` (Admin, IT, Operator) | path `accountNumber` string<br>**body** `ConnectivityRequest` { `reason` string, `correlationId` string? } |
| POST | `/api/v1/consumers/{consumerId:guid}/meter-replacement` | `Operations` (Admin, IT, Operator) | path `consumerId` guid<br>**body** `MeterReplacementApiRequest` { `newMeterNumber` string, `phase` MeterPhase (SinglePhase|ThreePhase), `effectiveFrom` date, `oldMeterClosingReadingKwh` number, `newMeterOpeningReadingKwh` number, `reason` string } |
| GET | `/api/v1/consumers/{consumerId:guid}/notifications` | Any signed-in user | path `consumerId` guid |
| GET | `/api/v1/consumers/search` | Any signed-in user | query `q` string (optional)<br>query `status` ConnectionStatus (Active|Disconnected|ReconnectionPending|DisconnectionPending) (optional)<br>query `lowBalance` bool (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |

## Conversions

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/conversions` | Any signed-in user | none |
| POST | `/api/v1/conversions` | `Operations` (Admin, IT, Operator) | **body** list of ConversionRequestItem |
| GET | `/api/v1/conversions/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/conversions/reverse` | Any signed-in user | none |
| POST | `/api/v1/conversions/reverse` | `Operations` (Admin, IT, Operator) | **body** `ReverseConversionApiRequest` { `consumerNumber` string, `reason` string, `requestedBy` string } |
| GET | `/api/v1/conversions/search` | Any signed-in user | query `q` string (optional)<br>query `status` string (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/conversions/summary` | Any signed-in user | none |

## Dashboard

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/dashboard/summary` | Any signed-in user | none |

## Exceptions

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/exceptions` | Any signed-in user | none |
| GET | `/api/v1/exceptions/{id:guid}` | Any signed-in user | path `id` guid |
| POST | `/api/v1/exceptions/{id:guid}/resolve` | `Operations` (Admin, IT, Operator) | path `id` guid<br>**body** `ResolutionRequest` { `note` string } |
| GET | `/api/v1/exceptions/search` | Any signed-in user | query `q` string (optional)<br>query `status` OperationalExceptionStatus (Open|Resolved) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/exceptions/summary` | Any signed-in user | none |

## Meter commands (meter credit)

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/meter-commands` | Any signed-in user | none |
| GET | `/api/v1/meter-commands/{id:guid}` | Any signed-in user | path `id` guid |
| POST | `/api/v1/meter-commands/{id:guid}/retry` | `Operations` (Admin, IT, Operator) | path `id` guid |
| GET | `/api/v1/meter-commands/search` | Any signed-in user | query `q` string (optional)<br>query `status` string (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/meter-commands/summary` | Any signed-in user | none |

## Meter data

| Method | Path | Access | Parameters |
|---|---|---|---|
| POST | `/api/v1/meter-data/{meterId:guid}/billing-hold/clear` | `Operations` (Admin, IT, Operator) | path `meterId` guid<br>**body** `ResolutionRequest` { `note` string } |
| GET | `/api/v1/meter-data/alarms` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional)<br>query `status` MeterAlarmStatus (Open|Acknowledged|Resolved) (optional) |
| POST | `/api/v1/meter-data/alarms` | `DataAdmin` (Admin, IT) | **body** `MeterAlarmIngestRequest` { `consumerId` guid, `meterId` guid, `alarmCode` MeterAlarmCode (Tamper|MagneticInfluence|CoverOpen|ReverseEnergy|VoltageAbnormality|CurrentAbnormality|Other), `severity` MeterAlarmSeverity (Info|Warning|Critical), `raisedAt` date, `sourceReference` string? } |
| POST | `/api/v1/meter-data/alarms/{id:guid}/acknowledge` | `Operations` (Admin, IT, Operator) | path `id` guid<br>**body** `AcknowledgeAlarmRequest` { `acknowledgedBy` string } |
| POST | `/api/v1/meter-data/alarms/{id:guid}/resolve` | `Operations` (Admin, IT, Operator) | path `id` guid<br>**body** `ResolveAlarmRequest` { `resolutionNote` string } |
| GET | `/api/v1/meter-data/alarms/search` | Any signed-in user | query `q` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `status` MeterAlarmStatus (Open|Acknowledged|Resolved) (optional)<br>query `severity` MeterAlarmSeverity (Info|Warning|Critical) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/meter-data/billing-holds` | Any signed-in user | query `activeOnly` bool (optional) |
| POST | `/api/v1/meter-data/billing-holds/clear-bulk` | `Operations` (Admin, IT, Operator) | **body** `BulkClearBillingHoldsRequest` { `meterIds` list of guid, `note` string } |
| GET | `/api/v1/meter-data/bp` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional) |
| POST | `/api/v1/meter-data/bp` | `DataAdmin` (Admin, IT) | **body** `RegisterReadingIngestRequest` { `consumerId` guid, `meterId` guid, `readingTimestamp` date, `cumulativeImportKwh` number, `sourceReference` string? } |
| GET | `/api/v1/meter-data/bp/search` | Any signed-in user | query `q` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `status` RegisterReadingStatus (Received|Validated|Rejected) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/meter-data/dlp` | Any signed-in user | query `consumerId` guid (optional) |
| POST | `/api/v1/meter-data/dlp` | `DataAdmin` (Admin, IT) | **body** `DailyLoadProfileIngestRequest` { `consumerId` guid, `meterId` guid, `profileDate` date, `generatedAt` date, `startCumulativeKwh` number, `endCumulativeKwh` number, `sourceReference` string? } |
| GET | `/api/v1/meter-data/dlp-completeness` | Any signed-in user | query `date` date |
| GET | `/api/v1/meter-data/dlp/search` | Any signed-in user | query `q` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `status` DailyProfileStatus (Received|Validated|Rejected|Billed|Provisional|Reconciled) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/meter-data/energy-validation` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional)<br>query `status` EnergyValidationStatus (Pass|Warning|Fail|Hold|Provisional) (optional) |
| POST | `/api/v1/meter-data/energy-validation` | `DataAdmin` (Admin, IT) | **body** `EvaluateEnergyValidationRequest` { `consumerId` guid, `meterId` guid, `validationDate` date } |
| GET | `/api/v1/meter-data/events` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional) |
| POST | `/api/v1/meter-data/events` | `DataAdmin` (Admin, IT) | **body** `MeterEventIngestRequest` { `consumerId` guid, `meterId` guid, `eventCode` MeterEventCode (PowerFailure|PowerRestoration|CommunicationFailure|CommunicationRestoration|RelayClosed|RelayOpened|MeterClockChanged|Other), `eventTimestamp` date, `description` string?, `sourceReference` string? } |
| GET | `/api/v1/meter-data/events/search` | Any signed-in user | query `q` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `eventCode` MeterEventCode (PowerFailure|PowerRestoration|CommunicationFailure|CommunicationRestoration|RelayClosed|RelayOpened|MeterClockChanged|Other) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| POST | `/api/v1/meter-data/ip` | `DataAdmin` (Admin, IT) | **body** `InstantaneousReadingIngestRequest` { `consumerId` guid, `meterId` guid, `timestamp` date, `voltageVolts` number, `currentAmps` number, `powerKw` number, `powerFactor` number, `frequencyHz` number, `relayStatus` MeterRelayStatus (Closed|Open), `sourceReference` string? } |
| GET | `/api/v1/meter-data/ip/latest` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional) |
| GET | `/api/v1/meter-data/ls` | Any signed-in user | query `consumerId` guid (optional)<br>query `meterId` guid (optional)<br>query `from` date (optional)<br>query `to` date (optional) |
| POST | `/api/v1/meter-data/ls` | `DataAdmin` (Admin, IT) | **body** `LoadSurveyIntervalIngestRequest` { `consumerId` guid, `meterId` guid, `intervalStart` date, `intervalEnd` date, `importKwh` number, `sourceReference` string? } |
| GET | `/api/v1/meter-data/ls/search` | Any signed-in user | query `q` string (optional)<br>query `from` date (optional)<br>query `to` date (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |

## Meter replacements

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/meter-replacements` | Any signed-in user | none |
| GET | `/api/v1/meter-replacements/search` | Any signed-in user | query `q` string (optional)<br>query `eventType` MeterAssignmentEventType (Installed|Replaced|Removed) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/meter-replacements/summary` | Any signed-in user | none |

## Network

| Method | Path | Access | Parameters |
|---|---|---|---|
| POST | `/api/v1/network/consumer-mapping` | `DataAdmin` (Admin, IT) | **body** `ConsumerMappingRequest` { `rows` list of ConsumerMappingRow?, `dryRun` bool } |
| POST | `/api/v1/network/import` | `DataAdmin` (Admin, IT) | **body** `NetworkImportRequest` { `rows` list of NetworkImportRow?, `dryRun` bool } |
| GET | `/api/v1/network/nodes` | Any signed-in user | query `level` string<br>query `parentId` guid (optional) |
| GET | `/api/v1/network/summary` | Any signed-in user | none |

## Notifications

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/notifications` | Any signed-in user | none |
| GET | `/api/v1/notifications/search` | Any signed-in user | query `q` string (optional)<br>query `eventType` NotificationEventType (LowBalance|EmergencyCredit|DisconnectionEligible|BillingProvisional|PrepaidConversionCompleted|AutoDisconnected|AutoReconnected) (optional)<br>query `status` NotificationStatus (Pending|Sent|Failed) (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/notifications/summary` | Any signed-in user | none |

## Platform

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/health` | Anonymous | none |

## Recharges

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/recharges` | Any signed-in user | query `accountNumber` string (optional) |
| GET | `/api/v1/recharges/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/recharges/search` | Any signed-in user | query `q` string (optional)<br>query `paymentStatus` RechargeStatus (Initiated|Success|Failed|Reversed) (optional)<br>query `meterCredit` string (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/recharges/summary` | Any signed-in user | none |

## Reconciliation adjustments

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/reconciliation-adjustments` | Any signed-in user | none |
| GET | `/api/v1/reconciliation-adjustments/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/reconciliation-adjustments/search` | Any signed-in user | query `q` string (optional)<br>query `after` string (optional)<br>query `pageSize` int (optional) |
| GET | `/api/v1/reconciliation-adjustments/summary` | Any signed-in user | none |

## Report jobs

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/report-jobs` | Any signed-in user | none |
| POST | `/api/v1/report-jobs` | `Operations` (Admin, IT, Operator) | **body** `ReportJobRequest` { `report` string?, `from` date?, `to` date?, `status` string?, `zoneId` guid?, `circleId` guid?, `divisionId` guid?, `subDivisionId` guid?, `substationId` guid?, `feederId` guid?, `dtrId` guid? } |
| GET | `/api/v1/report-jobs/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/report-jobs/{id:guid}/download` | `Operations` (Admin, IT, Operator) | path `id` guid |

## Reports

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/reports/billing` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional)<br>query `status` BillStatus (Generated|Paid|PartiallyPaid|Overdue|Cancelled) (optional)<br>query `ZoneId` guid (optional)<br>query `CircleId` guid (optional)<br>query `DivisionId` guid (optional)<br>query `SubDivisionId` guid (optional)<br>query `SubstationId` guid (optional)<br>query `FeederId` guid (optional)<br>query `DtrId` guid (optional) |
| GET | `/api/v1/reports/day-wise-rc-dc` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional)<br>query `level` string (optional)<br>query `ZoneId` guid (optional)<br>query `CircleId` guid (optional)<br>query `DivisionId` guid (optional)<br>query `SubDivisionId` guid (optional)<br>query `SubstationId` guid (optional)<br>query `FeederId` guid (optional)<br>query `DtrId` guid (optional) |
| GET | `/api/v1/reports/day-wise-recharge` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional)<br>query `level` string (optional)<br>query `ZoneId` guid (optional)<br>query `CircleId` guid (optional)<br>query `DivisionId` guid (optional)<br>query `SubDivisionId` guid (optional)<br>query `SubstationId` guid (optional)<br>query `FeederId` guid (optional)<br>query `DtrId` guid (optional) |
| GET | `/api/v1/reports/meter-credit-failures` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional)<br>query `ZoneId` guid (optional)<br>query `CircleId` guid (optional)<br>query `DivisionId` guid (optional)<br>query `SubDivisionId` guid (optional)<br>query `SubstationId` guid (optional)<br>query `FeederId` guid (optional)<br>query `DtrId` guid (optional) |
| GET | `/api/v1/reports/recharge-failures` | Any signed-in user | query `from` date (optional)<br>query `to` date (optional)<br>query `ZoneId` guid (optional)<br>query `CircleId` guid (optional)<br>query `DivisionId` guid (optional)<br>query `SubDivisionId` guid (optional)<br>query `SubstationId` guid (optional)<br>query `FeederId` guid (optional)<br>query `DtrId` guid (optional) |

## Risk indicators

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/risk-indicators` | Any signed-in user | none |

## SLA

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/sla` | Any signed-in user | none |

## System

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/system/health` | Any signed-in user | none |
| GET | `/api/v1/system/integrations` | Any signed-in user | none |

## Tariff change requests

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/tariff-change-requests` | Any signed-in user | query `status` TariffChangeRequestStatus (Draft|PendingApproval|Rejected|Scheduled|Activated|Cancelled) (optional) |
| POST | `/api/v1/tariff-change-requests` | `ITRole` (Admin, IT) | **body** `CreateTariffChangeRequestBody` { `supersedesTariffId` guid?, `proposedName` string, `proposedCategory` ConsumerCategory (Domestic|NonDomestic|GeneralPurpose|PublicWaterSupply|Industrial|FerroAlloy|Agriculture|Crematorium|ElectricVehicle|KutirJyotiBpl), `proposedSlabs` IReadOnlyList<TariffSlabInput>, `proposedFixedChargePerUnitPerMonth` number, `proposedPrepaidEnergyRebatePercent` number, `proposedEmergencyCreditLimit` number, `proposedMinVendAmountSinglePhase` number?, `proposedMaxVendAmountSinglePhase` number?, `proposedMinVendAmountThreePhase` number?, `proposedMaxVendAmountThreePhase` number?, `proposedTouPeriods` IReadOnlyList<TouPeriodInput>? } |
| GET | `/api/v1/tariff-change-requests/{id:guid}` | Any signed-in user | path `id` guid |
| POST | `/api/v1/tariff-change-requests/{id:guid}/approve` | `UtilityRole` (Utility) | path `id` guid<br>**body** `ApproveTariffChangeRequestBody` { `commencementDate` date } |
| POST | `/api/v1/tariff-change-requests/{id:guid}/cancel` | `TariffGovernanceRole` (Admin, IT, Utility) | path `id` guid<br>**body** `CancelTariffChangeRequestBody` { `reason` string } |
| PUT | `/api/v1/tariff-change-requests/{id:guid}/draft` | `ITRole` (Admin, IT) | path `id` guid<br>**body** `UpdateTariffChangeRequestBody` { `proposedName` string, `proposedCategory` ConsumerCategory (Domestic|NonDomestic|GeneralPurpose|PublicWaterSupply|Industrial|FerroAlloy|Agriculture|Crematorium|ElectricVehicle|KutirJyotiBpl), `proposedSlabs` IReadOnlyList<TariffSlabInput>, `proposedFixedChargePerUnitPerMonth` number, `proposedPrepaidEnergyRebatePercent` number, `proposedEmergencyCreditLimit` number, `proposedMinVendAmountSinglePhase` number?, `proposedMaxVendAmountSinglePhase` number?, `proposedMinVendAmountThreePhase` number?, `proposedMaxVendAmountThreePhase` number?, `proposedTouPeriods` IReadOnlyList<TouPeriodInput>? } |
| POST | `/api/v1/tariff-change-requests/{id:guid}/reject` | `UtilityRole` (Utility) | path `id` guid<br>**body** `RejectTariffChangeRequestBody` { `rejectionReason` string } |
| POST | `/api/v1/tariff-change-requests/{id:guid}/submit` | `ITRole` (Admin, IT) | path `id` guid<br>**body** `SubmitTariffChangeRequestBody` { `changeReason` string } |
| POST | `/api/v1/tariff-change-requests/activate-due` | `UtilityRole` (Utility) | none |

## Tariffs

| Method | Path | Access | Parameters |
|---|---|---|---|
| GET | `/api/v1/tariffs` | Any signed-in user | query `status` TariffLifecycleStatus (Active|Retired) (optional) |
| GET | `/api/v1/tariffs/{id:guid}` | Any signed-in user | path `id` guid |
| GET | `/api/v1/tariffs/{id:guid}/lineage` | Any signed-in user | path `id` guid |
| GET | `/api/v1/tariffs/{id:guid}/versions` | Any signed-in user | path `id` guid |
| POST | `/api/v1/tariffs/{id:guid}/versions` | `ITRole` (Admin, IT) | path `id` guid<br>**body** `TariffVersionRequest` { `fieldName` string, `oldValue` string, `newValue` string, `changeNote` string, `effectiveDate` date } |

