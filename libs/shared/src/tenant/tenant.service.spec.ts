import { TenantService } from './tenant.service';

/**
 * These cases mirror the gateway's HostSlugExtractor on purpose. If the two
 * ever disagree the UI will name one Samaaj while the API serves another, and
 * nothing would fail loudly.
 */
describe('TenantService.slugFromHost', () => {
  it('reads the first label of a Samaaj subdomain', () => {
    expect(TenantService.slugFromHost('mahavir-samaj.samaajconnect.com')).toBe('mahavir-samaj');
  });

  it('lowercases and trims', () => {
    expect(TenantService.slugFromHost('MAHAVIR-SAMAJ.samaajconnect.com.')).toBe('mahavir-samaj');
  });

  it('ignores a port', () => {
    expect(TenantService.slugFromHost('mahavir-samaj.samaajconnect.com:4200')).toBe('mahavir-samaj');
  });

  it('returns null on localhost, where there is no Samaaj', () => {
    expect(TenantService.slugFromHost('localhost')).toBeNull();
    expect(TenantService.slugFromHost('localhost:4200')).toBeNull();
  });

  it('returns null for an IP address', () => {
    expect(TenantService.slugFromHost('127.0.0.1')).toBeNull();
    expect(TenantService.slugFromHost('10.1.2.3:8080')).toBeNull();
  });

  it('returns null for a single-label internal hostname', () => {
    expect(TenantService.slugFromHost('gateway')).toBeNull();
  });

  it('returns null when there is no host at all', () => {
    expect(TenantService.slugFromHost(null)).toBeNull();
    expect(TenantService.slugFromHost('')).toBeNull();
    expect(TenantService.slugFromHost(undefined)).toBeNull();
  });
});
