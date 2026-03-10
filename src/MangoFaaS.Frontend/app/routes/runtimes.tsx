import { useState, useEffect, useCallback } from "react"
import { useAuth } from "~/lib/auth-context"
import { functionsApi, type Runtime, type CreateRuntimeResponse } from "~/lib/api"
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

const COMPRESSION_LABELS: Record<number, string> = { 0: "Deflate", 1: "Tar", 2: "None" }

export default function Runtimes() {
  const { token, isAdmin } = useAuth()

  const [runtimes, setRuntimes] = useState<Runtime[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Create runtime dialog
  const [createOpen, setCreateOpen] = useState(false)
  const [newName, setNewName] = useState("")
  const [newDesc, setNewDesc] = useState("")
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  // Upload + activate state (after creation)
  const [pendingRuntime, setPendingRuntime] = useState<CreateRuntimeResponse | null>(null)
  const [uploadFile, setUploadFile] = useState<File | null>(null)
  const [compression, setCompression] = useState("0") // Deflate
  const [activating, setActivating] = useState(false)
  const [activateError, setActivateError] = useState<string | null>(null)

  const loadData = useCallback(async () => {
    if (!token) return
    setLoading(true)
    setError(null)
    try {
      const rts = await functionsApi.getRuntimes(token)
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

  async function handleCreateRuntime(e: React.FormEvent) {
    e.preventDefault()
    if (!token) return
    setCreating(true)
    setCreateError(null)
    try {
      const res = await functionsApi.createRuntime(token, {
        name: newName,
        description: newDesc,
      })
      setPendingRuntime(res)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : "Failed to create")
    } finally {
      setCreating(false)
    }
  }

  async function handleUploadAndActivate(e: React.FormEvent) {
    e.preventDefault()
    if (!token || !pendingRuntime || !uploadFile) return
    setActivating(true)
    setActivateError(null)
    try {
      const uploadRes = await functionsApi.uploadFile(pendingRuntime.uploadUrl, uploadFile)
      if (!uploadRes.ok) throw new Error("Upload failed")

      await functionsApi.activateRuntime(token, pendingRuntime.id, {
        compressionMethod: parseInt(compression),
      })

      // Reset and close
      setPendingRuntime(null)
      setUploadFile(null)
      setCompression("0")
      setCreateOpen(false)
      setNewName("")
      setNewDesc("")
      await loadData()
    } catch (e) {
      setActivateError(e instanceof Error ? e.message : "Failed to activate")
    } finally {
      setActivating(false)
    }
  }

  if (!isAdmin) {
    return (
      <div className="p-6 md:p-8 max-w-6xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Runtimes</h1>
        </div>
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
          <p className="text-muted-foreground">Access restricted</p>
          <p className="text-sm text-muted-foreground/70 mt-1">
            Only administrators can manage runtimes.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="p-6 md:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Runtimes</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage execution runtimes for your functions.
          </p>
        </div>
        <Dialog
          open={createOpen}
          onOpenChange={(open) => {
            setCreateOpen(open)
            if (!open) {
              setPendingRuntime(null)
              setUploadFile(null)
              setCompression("0")
              setCreateError(null)
              setActivateError(null)
              setNewName("")
              setNewDesc("")
            }
          }}
        >
          <DialogTrigger asChild>
            <Button size="sm">Create runtime</Button>
          </DialogTrigger>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>
                {pendingRuntime ? "Upload runtime image" : "Create runtime"}
              </DialogTitle>
            </DialogHeader>

            {!pendingRuntime ? (
              <form onSubmit={handleCreateRuntime} className="flex flex-col gap-4">
                {createError && <p className="text-sm text-destructive">{createError}</p>}
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="rt-name">Name</Label>
                  <Input
                    id="rt-name"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    required
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="rt-desc">Description</Label>
                  <Input
                    id="rt-desc"
                    value={newDesc}
                    onChange={(e) => setNewDesc(e.target.value)}
                    required
                  />
                </div>
                <DialogFooter>
                  <Button type="submit" disabled={creating}>
                    {creating ? "Creating…" : "Create"}
                  </Button>
                </DialogFooter>
              </form>
            ) : (
              <form onSubmit={handleUploadAndActivate} className="flex flex-col gap-4">
                {activateError && <p className="text-sm text-destructive">{activateError}</p>}
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="rt-file">Runtime image (ext4)</Label>
                  <Input
                    id="rt-file"
                    type="file"
                    onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                    required
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label>Compression</Label>
                  <Select value={compression} onValueChange={setCompression}>
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="0">Deflate</SelectItem>
                      <SelectItem value="2">None</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <DialogFooter>
                  <Button type="submit" disabled={activating || !uploadFile}>
                    {activating ? "Uploading…" : "Upload & Activate"}
                  </Button>
                </DialogFooter>
              </form>
            )}
          </DialogContent>
        </Dialog>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {loading ? (
        <div className="flex items-center justify-center py-16">
          <p className="text-muted-foreground">Loading runtimes...</p>
        </div>
      ) : runtimes.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
          <p className="text-muted-foreground">No runtimes yet</p>
          <p className="text-sm text-muted-foreground/70 mt-1">
            Create a runtime to start deploying functions.
          </p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Compression</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {runtimes.map((rt) => (
              <TableRow key={rt.id}>
                <TableCell className="font-medium">{rt.name}</TableCell>
                <TableCell className="text-muted-foreground">{rt.description}</TableCell>
                <TableCell>
                  <Badge variant="outline">
                    {COMPRESSION_LABELS[rt.compressionMethod] ?? rt.compressionMethod}
                  </Badge>
                </TableCell>
                <TableCell>
                  <Badge variant={rt.isActive ? "default" : "secondary"}>
                    {rt.isActive ? "Active" : "Inactive"}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  )
}
