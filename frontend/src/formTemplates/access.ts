export function isFormTemplateAdministrator(roles: string[] | undefined) { return roles?.includes('ADMINISTRATOR') ?? false }
