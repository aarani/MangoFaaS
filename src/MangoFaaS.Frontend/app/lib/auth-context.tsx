import React, { createContext, useContext, useState, useEffect, useCallback, useRef } from "react"
import Keycloak from "keycloak-js"

interface AuthContextValue {
  token: string | null
  isAuthenticated: boolean
  isAdmin: boolean
  initialized: boolean
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

const keycloakInstance = new Keycloak({
  url: import.meta.env.VITE_KEYCLOAK_URL as string,
  realm: "api",
  clientId: "mango-frontend",
  scope: "api-gw-audience"
})

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [token, setToken] = useState<string | null>(null)
  const [isAdmin, setIsAdmin] = useState(false)
  const [initialized, setInitialized] = useState(false)
  const initCalled = useRef(false)

  const checkAdmin = useCallback(() => {
    const roles = keycloakInstance.realmAccess?.roles ?? []
    setIsAdmin(roles.includes("Admin"))
  }, [])

  useEffect(() => {
    if (initCalled.current) return
    initCalled.current = true

    keycloakInstance
      .init({ onLoad: "login-required", checkLoginIframe: false })
      .then((authenticated) => {
        if (authenticated) {
          setToken(keycloakInstance.token ?? null)
          checkAdmin()
        }
        setInitialized(true)
      })
      .catch((err) => {
        console.error("Keycloak init failed", err)
        setInitialized(true)
      })

    keycloakInstance.onTokenExpired = () => {
      keycloakInstance
        .updateToken(30)
        .then(() => {
          setToken(keycloakInstance.token ?? null)
          checkAdmin()
        })
        .catch(() => keycloakInstance.login())
    }
  }, [])

  const logout = useCallback(() => {
    keycloakInstance.logout({ redirectUri: window.location.origin })
  }, [])

  return (
    <AuthContext.Provider value={{ token, isAuthenticated: token !== null, isAdmin, initialized, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider")
  return ctx
}
