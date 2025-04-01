# Plexyfin Code Optimization Recommendations

Based on log analysis, here are recommendations to slim down the Plexyfin plugin code:

## 1. Reduce URL Pattern Attempts

The code tries too many URL patterns when fetching collection items, causing excessive API calls.

**Implementation:**
- Limit URL pattern attempts to 3-5 most common patterns
- Add configuration option to control maximum attempts
- Use prioritized list instead of trying all combinations

## 2. Make Debug Image Saving Optional

Currently saves debug copies of all images, consuming disk space unnecessarily.

**Implementation:**
- Add EnableDebugMode configuration option (default: false)
- Only save debug images when this option is enabled

## 3. Handle BlurHash Errors Gracefully

BlurHash computation errors shouldn't stop the process.

**Implementation:**
- Add try/catch blocks around metadata refresh operations
- Log errors but continue processing

## 4. Reduce Logging Verbosity

Many informational logs could be moved to debug level.

**Implementation:**
- Change non-essential LogInformation calls to LogDebug
- Keep important events at info level

## 5. Optimize Collection Path Handling

Standardize on one approach for collection paths.

**Implementation:**
- Use name-based paths consistently
- Consider caching path calculations

## Implementation Status

- [x] Added EnableDebugMode configuration option
- [x] Added MaxUrlPatternAttempts configuration option
- [x] Made debug image saving conditional
- [x] Added error handling for metadata refresh
- [ ] Reduced logging verbosity
- [ ] Simplified URL pattern attempts
- [ ] Optimized path handling
