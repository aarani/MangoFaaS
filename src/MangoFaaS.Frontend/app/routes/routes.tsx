import { useState, useEffect, useCallback } from "react"
import { useAuth } from "~/lib/auth-context"
import { gatewayApi, functionsApi, type Route, type FunctionItem, type FunctionVersion } from "~/lib/api"
import { Button } from "~/components/ui/button"
import { Input } from "~/components/ui/input"
import { Label } from "~/components/ui/label"
import { Badge } from "~/components/ui/badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "~/components/ui/table"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "~/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "~/components/ui/select"

const ROUTE_TYPES = [
  { value: "0", label: "Exact" },
  { value: "1", label: "Prefix" },
  { value: "2", label: "Regex" },
]

const ROUTE_TYPE_LABELS: Record<number, string> = { 0: "Exact", 1: "Prefix", 2: "Regex" }

function RouteForm({
  initial,
  onSubmit,
  submitting,
  error,
  functions,
  token,
}: {
  initial?: Partial<Route>
  onSubmit: (data: {
    host: string
    data: string
    functionId: string
    functionVersion: string
    type: number
  }) => void
  submitting: boolean
  error: string | null
  functions: FunctionItem[]
  token: string | null
}) {
  const [host, setHost] = useState(initial?.host ?? "")
  const [data, setData] = useState(initial?.data ?? "")
  const [functionId, setFunctionId] = useState(initial?.functionId ?? "")
  const [functionVersion, setFunctionVersion] = useState(initial?.functionVersion ?? "")
  const [type, setType] = useState(String(initial?.type ?? "0"))

  const [deployedVersions, setDeployedVersions] = useState<FunctionVersion[]>([])
  const [versionsLoading, setVersionsLoading] = useState(false)

  useEffect(() => {
    if (!functionId || !token) {
      setDeployedVersions([])
      return
    }
    let cancelled = false
    setVersionsLoading(true)
    functionsApi.getVersions(token, functionId).then((vs) => {
      if (!cancelled) {
        setDeployedVersions(vs.filter((v) => v.state === "Deployed"))
        setVersionsLoading(false)
      }
    }).catch(() => {
      if (!cancelled) {
        setDeployedVersions([])
        setVersionsLoading(false)
      }
    })
    return () => { cancelled = true }
  }, [functionId, token])

  function handleFunctionChange(id: string) {
    setFunctionId(id)
    setFunctionVersion("")
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    onSubmit({ host, data, functionId, functionVersion, type: Number(type) })
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-3">
      {error && <p className="text-sm text-destructive">{error}</p>}
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="r-host">Host</Label>
          <Input id="r-host" value={host} onChange={(e) => setHost(e.target.value)} required />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="r-data">Data (path)</Label>
          <Input id="r-data" value={data} onChange={(e) => setData(e.target.value)} required />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Function</Label>
          <Select value={functionId} onValueChange={handleFunctionChange}>
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Select a function" />
            </SelectTrigger>
            <SelectContent>
              {functions.map((fn) => (
                <SelectItem key={fn.id} value={fn.id}>
                  {fn.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Function version</Label>
          <Select
            value={functionVersion}
            onValueChange={setFunctionVersion}
            disabled={!functionId || versionsLoading}
          >
            <SelectTrigger className="w-full">
              <SelectValue placeholder={versionsLoading ? "Loading…" : "Select a version"} />
            </SelectTrigger>
            <SelectContent>
              {deployedVersions.map((v) => (
                <SelectItem key={v.id} value={v.id}>
                  {v.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label>Route type</Label>
        <Select value={type} onValueChange={setType}>
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {ROUTE_TYPES.map((rt) => (
              <SelectItem key={rt.value} value={rt.value}>
                {rt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <DialogFooter>
        <Button type="submit" disabled={submitting || !functionId || !functionVersion}>
          {submitting ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>
    </form>
  )
}

export default function Routes() {
  const { token } = useAuth()

  const [routes, setRoutes] = useState<Route[]>([])
  const [functions, setFunctions] = useState<FunctionItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Create dialog
  const [createOpen, setCreateOpen] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  // Edit dialog
  const [editRoute, setEditRoute] = useState<Route | null>(null)
  const [editError, setEditError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)

  // Delete confirm dialog
  const [deleteRoute, setDeleteRoute] = useState<Route | null>(null)
  const [deleting, setDeleting] = useState(false)

  const loadRoutes = useCallback(async () => {
    if (!token) return
    setLoading(true)
    setError(null)
    try {
      const [rs, fns] = await Promise.all([
        gatewayApi.getRoutes(token),
        functionsApi.getFunctions(token),
      ])
      setRoutes(rs)
      setFunctions(fns)
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load routes")
    } finally {
      setLoading(false)
    }
  }, [token])

  useEffect(() => {
    loadRoutes()
  }, [loadRoutes])

  async function handleCreate(data: Parameters<typeof gatewayApi.createRoute>[1]) {
    if (!token) return
    setCreating(true)
    setCreateError(null)
    try {
      await gatewayApi.createRoute(token, data)
      setCreateOpen(false)
      await loadRoutes()
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : "Failed to create route")
    } finally {
      setCreating(false)
    }
  }

  async function handleEdit(data: Parameters<typeof gatewayApi.updateRoute>[2]) {
    if (!token || !editRoute) return
    setEditing(true)
    setEditError(null)
    try {
      await gatewayApi.updateRoute(token, editRoute.id, data)
      setEditRoute(null)
      await loadRoutes()
    } catch (e) {
      setEditError(e instanceof Error ? e.message : "Failed to update route")
    } finally {
      setEditing(false)
    }
  }

  async function handleDelete() {
    if (!token || !deleteRoute) return
    setDeleting(true)
    try {
      await gatewayApi.deleteRoute(token, deleteRoute.id)
      setDeleteRoute(null)
      await loadRoutes()
    } catch {
      // ignore
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="p-6 md:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Gateway Routes</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Configure how incoming requests are routed to your functions.
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          Create route
        </Button>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {loading ? (
        <div className="flex items-center justify-center py-16">
          <p className="text-muted-foreground">Loading routes...</p>
        </div>
      ) : routes.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
          <p className="text-muted-foreground">No routes yet</p>
          <p className="text-sm text-muted-foreground/70 mt-1">
            Create a route to start directing traffic to your functions.
          </p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Host</TableHead>
              <TableHead>Path</TableHead>
              <TableHead>Function</TableHead>
              <TableHead>Version</TableHead>
              <TableHead>Type</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {routes.map((r) => (
              <TableRow key={r.id}>
                <TableCell className="font-medium">{r.host}</TableCell>
                <TableCell className="font-mono text-xs">{r.data}</TableCell>
                <TableCell>
                  {functions.find((fn) => fn.id === r.functionId)?.name ?? <span className="font-mono text-xs text-muted-foreground">{r.functionId}</span>}
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground max-w-40 truncate">{r.functionVersion}</TableCell>
                <TableCell>
                  <Badge variant="outline">{ROUTE_TYPE_LABELS[r.type] ?? r.type}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  <div className="flex items-center justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setEditRoute(r)}>
                      Edit
                    </Button>
                    <Button variant="destructive" size="sm" onClick={() => setDeleteRoute(r)}>
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {/* Create dialog */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Create route</DialogTitle>
          </DialogHeader>
          <RouteForm onSubmit={handleCreate} submitting={creating} error={createError} functions={functions} token={token} />
        </DialogContent>
      </Dialog>

      {/* Edit dialog */}
      <Dialog open={editRoute !== null} onOpenChange={(o) => !o && setEditRoute(null)}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Edit route</DialogTitle>
          </DialogHeader>
          {editRoute && (
            <RouteForm
              initial={editRoute}
              onSubmit={handleEdit}
              submitting={editing}
              error={editError}
              functions={functions}
              token={token}
            />
          )}
        </DialogContent>
      </Dialog>

      {/* Delete confirm dialog */}
      <Dialog open={deleteRoute !== null} onOpenChange={(o) => !o && setDeleteRoute(null)}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>Delete route</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Are you sure you want to delete the route for{" "}
            <span className="font-medium text-foreground">{deleteRoute?.host}</span>? This cannot
            be undone.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteRoute(null)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={handleDelete} disabled={deleting}>
              {deleting ? "Deleting…" : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
