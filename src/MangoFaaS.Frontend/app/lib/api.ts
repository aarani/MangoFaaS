const FUNCTIONS_URL = import.meta.env.VITE_FUNCTIONS_URL as string
const GATEWAY_URL = import.meta.env.VITE_GATEWAY_URL as string
const SECRETS_URL = import.meta.env.VITE_SECRETS_URL as string

async function apiFetch<T>(
  url: string,
  options: RequestInit = {},
  token?: string,
): Promise<T> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options.headers as Record<string, string>),
  }
  if (token) {
    headers["Authorization"] = `Bearer ${token}`
  }
  const res = await fetch(url, { ...options, headers })
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new Error(text || `HTTP ${res.status}`)
  }
  const contentType = res.headers.get("content-type") ?? ""
  if (contentType.includes("application/json")) {
    return res.json() as Promise<T>
  }
  return res.text() as unknown as T
}

export interface Runtime {
  id: string
  name: string
  description: string
  fileName: string
  compressionMethod: number
  isActive: boolean
}

export interface CreateRuntimeResponse {
  id: string
  uploadUrl: string
}

export interface FunctionItem {
  id: string
  name: string
  description: string
  runtime: string
  ownerId: string
}

export interface FunctionVersion {
  id: string
  name: string
  description: string
  entrypoint: string
  filePath: string
  compressionMethod: number
  state: string
  functionId: string
}

export interface CreateFunctionVersionResponse {
  id: string
  presignedUploadUrl: string
}

export interface Route {
  id: string
  tenantId: string
  host: string
  data: string
  functionId: string
  functionVersion: string
  type: number
}

export const functionsApi = {
  getRuntimes(token: string) {
    return apiFetch<Runtime[]>(`${FUNCTIONS_URL}/api/runtimes`, {}, token)
  },
  getFunctions(token: string) {
    return apiFetch<FunctionItem[]>(`${FUNCTIONS_URL}/api/functions`, {}, token)
  },
  getVersions(token: string, id: string) {
    return apiFetch<FunctionVersion[]>(`${FUNCTIONS_URL}/api/functions/${id}/versions`, {}, token)
  },
  createFunction(
    token: string,
    payload: { name: string; description: string; runtime: string },
  ) {
    return apiFetch<{ id: string }>(`${FUNCTIONS_URL}/api/functions`, {
      method: "PUT",
      body: JSON.stringify(payload),
    }, token)
  },
  createVersion(
    token: string,
    payload: {
      functionId: string
      name: string
      description: string
      entrypoint: string
    },
  ) {
    return apiFetch<CreateFunctionVersionResponse>(
      `${FUNCTIONS_URL}/api/functions/version`,
      { method: "PUT", body: JSON.stringify(payload) },
      token,
    )
  },
  createRuntime(
    token: string,
    payload: { name: string; description: string },
  ) {
    return apiFetch<CreateRuntimeResponse>(`${FUNCTIONS_URL}/api/runtimes`, {
      method: "PUT",
      body: JSON.stringify(payload),
    }, token)
  },
  activateRuntime(
    token: string,
    id: string,
    payload: { compressionMethod: number },
  ) {
    return apiFetch<void>(`${FUNCTIONS_URL}/api/runtimes/${id}/activate`, {
      method: "PATCH",
      body: JSON.stringify(payload),
    }, token)
  },
  uploadFile(presignedUrl: string, file: File) {
    return fetch(presignedUrl, {
      method: "PUT",
      body: file,
      headers: { "Content-Type": "application/octet-stream" },
    })
  },
}

export interface SecretItem {
  id: string
  name: string
  description: string | null
  createdAt: string
  updatedAt: string
}

export interface SecretValue extends SecretItem {
  value: string
}

export interface FunctionSecretBinding {
  id: string
  functionId: string
  secretId: string
  secretName: string
}

export const secretsApi = {
  getSecrets(token: string) {
    return apiFetch<SecretItem[]>(`${SECRETS_URL}/api/secrets`, {}, token)
  },
  getSecret(token: string, id: string) {
    return apiFetch<SecretValue>(`${SECRETS_URL}/api/secrets/${id}`, {}, token)
  },
  createSecret(
    token: string,
    payload: { name: string; value: string; description?: string },
  ) {
    return apiFetch<SecretItem>(`${SECRETS_URL}/api/secrets`, {
      method: "PUT",
      body: JSON.stringify(payload),
    }, token)
  },
  updateSecret(
    token: string,
    id: string,
    payload: { value?: string; description?: string },
  ) {
    return apiFetch<SecretItem>(`${SECRETS_URL}/api/secrets/${id}`, {
      method: "PATCH",
      body: JSON.stringify(payload),
    }, token)
  },
  deleteSecret(token: string, id: string) {
    return apiFetch<unknown>(`${SECRETS_URL}/api/secrets/${id}`, {
      method: "DELETE",
    }, token)
  },
  getFunctionSecrets(token: string, functionId: string) {
    return apiFetch<FunctionSecretBinding[]>(
      `${SECRETS_URL}/api/functions/${functionId}/secrets`, {}, token,
    )
  },
  addSecretToFunction(token: string, functionId: string, secretId: string) {
    return apiFetch<FunctionSecretBinding>(
      `${SECRETS_URL}/api/functions/${functionId}/secrets/${secretId}`,
      { method: "PUT" },
      token,
    )
  },
  removeSecretFromFunction(token: string, functionId: string, secretId: string) {
    return apiFetch<unknown>(
      `${SECRETS_URL}/api/functions/${functionId}/secrets/${secretId}`,
      { method: "DELETE" },
      token,
    )
  },
}

export const gatewayApi = {
  getRoutes(token: string) {
    return apiFetch<Route[]>(`${GATEWAY_URL}/api/routes`, {}, token)
  },
  createRoute(
    token: string,
    payload: {
      host: string
      data: string
      functionId: string
      functionVersion: string
      type: number
    },
  ) {
    return apiFetch<unknown>(`${GATEWAY_URL}/api/routes`, {
      method: "POST",
      body: JSON.stringify(payload),
    }, token)
  },
  updateRoute(
    token: string,
    id: string,
    payload: {
      host?: string
      data?: string
      functionId?: string
      functionVersion?: string
      type?: number
    },
  ) {
    return apiFetch<unknown>(`${GATEWAY_URL}/api/routes/${id}`, {
      method: "PUT",
      body: JSON.stringify(payload),
    }, token)
  },
  deleteRoute(token: string, id: string) {
    return apiFetch<unknown>(`${GATEWAY_URL}/api/routes/${id}`, {
      method: "DELETE",
    }, token)
  },
}
