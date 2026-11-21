import type { components } from './openapi'

type Schemas = components['schemas']

export type UserRole = Schemas['UserRole']
export type DeviceKind = Schemas['DeviceKind']
export type HealthState = Schemas['HealthState']
export type RemovalBehavior = Schemas['RemovalBehavior']
export type ActionStatus = Schemas['ActionStatus']
export type SubtitleRequestStatus = Schemas['SubtitleRequestStatus']
export type PluginStatus = Schemas['PluginStatus']

export type HealthResponse = Schemas['HealthResponse']
export type SetupStatus = Schemas['SetupStatus']
export type SetupResponse = Schemas['SetupResponse']

export type UserSummary = Schemas['UserSummary']
export type DeviceSummary = Schemas['DeviceSummary']
export type CurrentUser = Schemas['CurrentUser']
export type ApiKeySummary = Schemas['ApiKeySummary']
export type CreatedApiKey = Schemas['CreatedApiKey']

export type PairingStarted = Schemas['PairingStarted']
export type PairingCompleted = Schemas['PairingCompleted']
export type PairingPending = Schemas['PairingPending']
export type PendingPairing = Schemas['PendingPairing']

export type EffectiveRemoval = Schemas['EffectiveRemoval']
export type ServerRemovalSettings = Schemas['ServerRemovalSettings']
export type AuditEntry = Schemas['AuditEntryModel']
export type AuditPage = Schemas['PagedResponseOfAuditEntryModel']

export type SubtitleRequest = Schemas['SubtitleRequestModel']
export type SubtitleProvider = Schemas['SubtitleProviderModel']

export type EventMessage = Schemas['EventMessage']
export type OutgoingWebhook = Schemas['OutgoingWebhookSummary']
export type CreatedOutgoingWebhook = Schemas['CreatedOutgoingWebhook']
export type OutgoingWebhookTestResult = Schemas['OutgoingWebhookTestResult']

export type TraktStatus = Schemas['TraktStatus']
export type TraktImportResult = Schemas['TraktImportResult']

export type NowPlaying = Schemas['NowPlaying']
export type NowPlayingTrack = Schemas['NowPlayingTrack']
export type TranscoderSample = Schemas['TranscoderSample']

export type PluginOverview = Schemas['PluginOverview']
export type PluginSummary = Schemas['PluginSummary']
export type BridgeSummary = Schemas['BridgeSummary']
export type LinkCreated = Schemas['LinkCreated']
export type ProblemDetails = Schemas['HttpValidationProblemDetails']
