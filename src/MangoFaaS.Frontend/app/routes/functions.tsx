import { useState, useEffect, useCallback } from "react"
import { useAuth } from "~/lib/auth-context"
import { functionsApi, type FunctionItem, type FunctionVersion, type Runtime } from "~/lib/api"
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
  DialogTrigger,
  DialogFooter,
} from "~/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "~/components/ui/select"

const STATE_LABELS: Record<number, string> = { 0: "Created", 1: "Deployed", 2: "Failed" }
const STATE_VARIANTS: Record<number, "default" | "secondary" | "destructive"> = {
  0: "secondary",
  1: "default",
  2: "destructive",
}

export default function Functions() {
  const { token } = useAuth()

  const [functions, setFunctions] = useState<FunctionItem[]>([])
  const [runtimes, setRuntimes] = useState<Runtime[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Create function dialog
  const [createOpen, setCreateOpen] = useState(false)
  const [newName, setNewName] = useState("")
  const [newDesc, setNewDesc] = useState("")
  const [newRuntime, setNewRuntime] = useState("")
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  // Versions dialog
  const [versionsOpen, setVersionsOpen] = useState(false)
  const [selectedFn, setSelectedFn] = useState<FunctionItem | null>(null)
  const [versions, setVersions] = useState<FunctionVersion[]>([])
  const [versionsLoading, setVersionsLoading] = useState(false)

  // Create version form
  const [vName, setVName] = useState("")
  const [vDesc, setVDesc] = useState("")
  const [vEntrypoint, setVEntrypoint] = useState("")
  const [vFile, setVFile] = useState<File | null>(null)
  const [vCreating, setVCreating] = useState(false)
  const [vError, setVError] = useState<string | null>(null)

  const loadData = useCallback(async () => {
    if (!token) return
    setLoading(true)
    setError(null)
    try {
      const [fns, rts] = await Promise.all([
        functionsApi.getFunctions(token),
        functionsApi.getRuntimes(token),
      ])
      setFunctions(fns)
      setRuntimes(rts)
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load")
    } finally {
      setLoading(false)
    }
  }, [token])

  useEffect(() => {
    loadData()
  }, [loadData])

  async function handleCreateFunction(e: React.FormEvent) {
    e.preventDefault()
    if (!token) return
    setCreating(true)
    setCreateError(null)
    try {
      await functionsApi.createFunction(token, {
        name: newName,
        description: newDesc,
        runtime: newRuntime,
      })
      setCreateOpen(false)
      setNewName("")
      setNewDesc("")
      setNewRuntime("")
      await loadData()
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : "Failed to create")
    } finally {
      setCreating(false)
    }
  }

  async function openVersions(fn: FunctionItem) {
    if (!token) return
    setSelectedFn(fn)
    setVersionsOpen(true)
    setVersionsLoading(true)
    setVError(null)
    try {
      const vs = await functionsApi.getVersions(token, fn.id)
      setVersions(vs)
    } catch {
      setVersions([])
    } finally {
      setVersionsLoading(false)
    }
  }

  async function handleCreateVersion(e: React.FormEvent) {
    e.preventDefault()
    if (!token || !selectedFn) return
    setVCreating(true)
    setVError(null)
    try {
      const res = await functionsApi.createVersion(token, {
        functionId: selectedFn.id,
        name: vName,
        description: vDesc,
        entrypoint: vEntrypoint,
      })
      if (vFile) {
        await functionsApi.uploadFile(res.presignedUploadUrl, vFile)
      }
      const vs = await functionsApi.getVersions(token, selectedFn.id)
      setVersions(vs)
      setVName("")
      setVDesc("")
      setVEntrypoint("")
      setVFile(null)
    } catch (e) {
      setVError(e instanceof Error ? e.message : "Failed to create version")
    } finally {
      setVCreating(false)
    }
  }

  const runtimeName = (runtimeId: string) =>
    runtimes.find((r) => r.id === runtimeId)?.name ?? runtimeId

  return (
    <div className="p-6 md:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Functions</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage your serverless functions and their versions.
          </p>
        </div>
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger asChild>
            <Button>Create function</Button>
          </DialogTrigger>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Create function</DialogTitle>
            </DialogHeader>
            <form onSubmit={handleCreateFunction} className="flex flex-col gap-4">
              {createError && <p className="text-sm text-destructive">{createError}</p>}
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="fn-name">Name</Label>
                <Input
                  id="fn-name"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  required
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="fn-desc">Description</Label>
                <Input
                  id="fn-desc"
                  value={newDesc}
                  onChange={(e) => setNewDesc(e.target.value)}
                  required
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Runtime</Label>
                <Select value={newRuntime} onValueChange={setNewRuntime} required>
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="Select a runtime" />
                  </SelectTrigger>
                  <SelectContent>
                    {runtimes.map((r) => (
                      <SelectItem key={r.id} value={r.id}>
                        {r.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <DialogFooter>
                <Button type="submit" disabled={creating || !newRuntime}>
                  {creating ? "Creating…" : "Create"}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {loading ? (
        <div className="flex items-center justify-center py-16">
          <p className="text-muted-foreground">Loading functions...</p>
        </div>
      ) : functions.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
          <p className="text-muted-foreground">No functions yet</p>
          <p className="text-sm text-muted-foreground/70 mt-1">
            Get started by creating your first function.
          </p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Runtime</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {functions.map((fn) => (
              <TableRow key={fn.id}>
                <TableCell className="font-medium">{fn.name}</TableCell>
                <TableCell className="text-muted-foreground">{fn.description}</TableCell>
                <TableCell>
                  <Badge variant="outline">{runtimeName(fn.runtime)}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  <Button variant="outline" size="sm" onClick={() => openVersions(fn)}>
                    Versions
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {/* Versions dialog */}
      <Dialog
        open={versionsOpen}
        onOpenChange={(open) => {
          setVersionsOpen(open)
          if (!open) {
            setVName("")
            setVDesc("")
            setVEntrypoint("")
            setVFile(null)
            setVError(null)
          }
        }}
      >
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>Versions — {selectedFn?.name}</DialogTitle>
          </DialogHeader>

          {versionsLoading ? (
            <div className="flex items-center justify-center py-8">
              <p className="text-muted-foreground">Loading versions...</p>
            </div>
          ) : versions.length === 0 ? (
            <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-8 text-center">
              <p className="text-sm text-muted-foreground">No versions yet</p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Entrypoint</TableHead>
                  <TableHead>State</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {versions.map((v) => (
                  <TableRow key={v.id}>
                    <TableCell>{v.name}</TableCell>
                    <TableCell className="font-mono text-xs">{v.entrypoint}</TableCell>
                    <TableCell>
                      <Badge variant={STATE_VARIANTS[v.state] ?? "secondary"}>
                        {STATE_LABELS[v.state] ?? v.state}
                      </Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          <div className="border-t pt-4">
            <p className="text-sm font-medium mb-3">Create version</p>
            <form onSubmit={handleCreateVersion} className="flex flex-col gap-3">
              {vError && <p className="text-sm text-destructive">{vError}</p>}
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="v-name">Name</Label>
                  <Input
                    id="v-name"
                    value={vName}
                    onChange={(e) => setVName(e.target.value)}
                    required
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="v-entrypoint">Entrypoint</Label>
                  <Input
                    id="v-entrypoint"
                    value={vEntrypoint}
                    onChange={(e) => setVEntrypoint(e.target.value)}
                    required
                  />
                </div>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="v-desc">Description</Label>
                <Input
                  id="v-desc"
                  value={vDesc}
                  onChange={(e) => setVDesc(e.target.value)}
                  required
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="v-file">Function file</Label>
                <Input
                  id="v-file"
                  type="file"
                  onChange={(e) => setVFile(e.target.files?.[0] ?? null)}
                  required
                />
              </div>
              <div className="flex justify-end">
                <Button type="submit" size="sm" disabled={vCreating}>
                  {vCreating ? "Uploading…" : "Create version"}
                </Button>
              </div>
            </form>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  )
}
