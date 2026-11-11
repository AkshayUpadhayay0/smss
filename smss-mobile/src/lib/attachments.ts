import { Directory, File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { api } from '../api';
import { ApiError } from '../api/errors';

const FOLDER = 'attachments';

function safeName(id: number, name: string | null): string {
  const base = (name ?? 'attachment').replace(/[^\w.\- ]+/g, '_').slice(0, 80) || 'attachment';
  return `${id}-${base}`;
}

/**
 * Downloads a homework attachment with an AUTHENTICATED request (the file is private: no plain URL ever), saves it in the cache directory
 * and hands it to the system share / open sheet.
 */
export async function openHomeworkAttachment(homeworkId: number, fileName: string | null): Promise<void> {
  const { bytes, contentType } = await api.download(`/api/Homework/attachment/${homeworkId}`);

  const dir = new Directory(Paths.cache, FOLDER);
  if (!dir.exists) dir.create();
  const file = new File(dir, safeName(homeworkId, fileName));
  if (file.exists) file.delete();
  file.create();
  file.write(new Uint8Array(bytes));

  if (!(await Sharing.isAvailableAsync())) {
    throw new ApiError('unknown', "This device can't open files from the app.");
  }
  await Sharing.shareAsync(file.uri, { mimeType: contentType, dialogTitle: fileName ?? 'Attachment' });
}

/** Removes every downloaded attachment (called on sign-out: the next user must not find the previous user's files). */
export function clearAttachmentCache(): void {
  try {
    const dir = new Directory(Paths.cache, FOLDER);
    if (dir.exists) dir.delete();
  } catch {
    // best effort: the OS also clears the cache directory
  }
}
