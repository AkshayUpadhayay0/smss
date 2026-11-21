import { DatePipe, DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PRIVACY_LAST_UPDATED, SITE_CONFIG } from '../../../core/config/site.config';

interface TocEntry {
  id: string;
  title: string;
}

/** Order and wording of the contents list; every id matches an <h2 id> in the template below. */
export const PRIVACY_TOC: TocEntry[] = [
  { id: 'who-we-are', title: 'Who we are and what this covers' },
  { id: 'what-we-collect', title: 'Information we collect' },
  { id: 'how-we-use-it', title: 'How we use it' },
  { id: 'school-data', title: 'School platform data' },
  { id: 'permissions', title: 'Permissions the app asks for' },
  { id: 'location', title: 'Location permission: Driver live tracking' },
  { id: 'sharing', title: 'Sharing and third parties' },
  { id: 'retention', title: 'How long we keep it' },
  { id: 'security', title: 'How we protect it' },
  { id: 'your-rights', title: 'Your rights and how to make a request' },
  { id: 'children', title: "Children's privacy" },
  { id: 'changes', title: 'Changes to this policy' },
  { id: 'contact', title: 'Contact us' },
];

/**
 * The public Privacy Policy for the SMSS website and the SMSS mobile app. Prerendered to static HTML (no login).
 * It states only what the code does. The developer name, address, email and phone come from site.config.ts; the date from PRIVACY_LAST_UPDATED.
 */
@Component({
  selector: 'app-privacy-policy',
  imports: [RouterLink, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './privacy-policy.component.scss',
  template: `
    <article class="pub-container doc">
      <h1>Privacy Policy</h1>
      <p class="updated">Last updated: <time [attr.datetime]="updated">{{ updated | date: 'd MMMM y' }}</time></p>

      <nav class="toc" aria-labelledby="toc-title">
        <h2 id="toc-title">Contents</h2>
        <ol>
          @for (t of toc; track t.id) {
            <li><a class="pub-link" [routerLink]="[]" [fragment]="t.id" (click)="focusSection(t.id)">{{ t.title }}</a></li>
          }
        </ol>
      </nav>

      <section aria-labelledby="who-we-are">
        <h2 id="who-we-are" tabindex="-1">1. Who we are and what this covers</h2>
        <p>
          {{ site.name }} is a school management platform. It is provided by {{ site.legalName }} ("we", "us"), {{ site.address }}.
          Schools use it to run admissions, students, attendance, examinations, fees, transport and communication.
        </p>
        <p>This policy covers:</p>
        <ul>
          <li><strong>the {{ site.name }} website</strong> at {{ site.url }}, including its Contact Us and Request a Demo forms; and</li>
          <li><strong>the {{ site.name }} mobile app</strong>, used by the students, teachers and drivers of schools that use {{ site.name }}.</li>
        </ul>
        <p>It explains what we collect, why, who can see it, and the choices you have.</p>
      </section>

      <section aria-labelledby="what-we-collect">
        <h2 id="what-we-collect" tabindex="-1">2. Information we collect</h2>
        <h3>On the website</h3>
        <p>
          Browsing the public pages requires nothing from you. If you send a Contact Us message or a Request a Demo form we collect what you type: your name, email address and,
          depending on the form, your designation, school name, phone number, city, approximate number of students, a subject, your message and a preferred demo date and time.
          We also record that you agreed to this policy, the time of the submission, your IP address and your browser's "user agent" text (which names your browser and device type), to answer you and to prevent spam and abuse.
          The forms contain a hidden field that only automated programs fill in; if it is filled in, nothing from that submission is stored.
        </p>
        <h3>In the app and on the platform</h3>
        <ul>
          <li><strong>Account details.</strong> Your school creates your account. We hold your name, login ID, role (for example student, teacher or driver) and the contact details your school gave us, such as an email address or mobile number. Your password is stored only as a one-way hash.</li>
          <li><strong>Student, guardian and staff records.</strong> The school's records that the app shows or updates: for example class and section, attendance, homework, marks, fees, leave, notices, timetables and transport details. See <a class="pub-link" [routerLink]="[]" fragment="school-data" (click)="focusSection('school-data')">section 4</a>.</li>
          <li><strong>Device and notification identifiers.</strong> To send you notifications the app registers a push-notification token with us, together with the device type (Android or iOS), a device label and the app version. The token is removed when you sign out.</li>
          <li><strong>Files you choose to attach.</strong> A teacher can attach a file to a homework item. The app reads a file only when you pick it. The app does not use your camera or microphone and does not read your contacts or messages.</li>
          <li><strong>Location, for drivers only.</strong> See <a class="pub-link" [routerLink]="[]" fragment="location" (click)="focusSection('location')">section 6</a>. No other user's location is collected.</li>
          <li><strong>Technical data.</strong> Like most online services, our servers receive the IP address and device or browser details of each request. We use them to run the service, to limit repeated sign-in attempts and to prevent abuse.</li>
        </ul>
        <h3>Cookies and browser storage</h3>
        <p>
          The public website sets no cookies and stores nothing in your browser. When a school user signs in on the web, the sign-in session is kept in the browser's storage so that you stay signed in; in the app it is kept in the device's secure storage.
          We do not use advertising or analytics trackers.
        </p>
      </section>

      <section aria-labelledby="how-we-use-it">
        <h2 id="how-we-use-it" tabindex="-1">3. How we use it</h2>
        <ul>
          <li>To provide the platform and the app to the school you belong to, and to let you sign in and use the features your role allows.</li>
          <li>To send you notifications from your school, such as fee reminders and notices.</li>
          <li>For drivers: to show the school the vehicle's current position during an active duty.</li>
          <li>To reply to website enquiries and arrange demos.</li>
          <li>To keep the service secure, to prevent spam and misuse, and to fix problems.</li>
        </ul>
        <p>We do not use your information for advertising and we do not sell it. For website enquiries, our basis is your consent, which you can withdraw at any time (see section 10).</p>
      </section>

      <section aria-labelledby="school-data">
        <h2 id="school-data" tabindex="-1">4. School platform data</h2>
        <p>
          The student, guardian and staff information in {{ site.name }} belongs to the school. <strong>The school decides</strong> what is collected and how it is used, and is responsible for telling parents, guardians and staff about it and for any consent that is needed.
          We process this information on the school's behalf and on its instructions, to provide the platform.
        </p>
        <p>
          Information about students, who are often children, is handled only through the school that enrolled them. We do not collect it through the public website and we do not contact children ourselves.
        </p>
      </section>

      <section aria-labelledby="permissions">
        <h2 id="permissions" tabindex="-1">5. Permissions the app asks for</h2>
        <ul>
          <li><strong>Notifications:</strong> to show school notices and reminders on your phone.</li>
          <li><strong>Location (precise, approximate and background) and a foreground service:</strong> asked of drivers only, and used only for live tracking during an active duty. See the next section. Students and teachers are never asked for location.</li>
          <li><strong>Internet access:</strong> to talk to the {{ site.name }} servers.</li>
          <li><strong>Files:</strong> on older Android versions the system may ask for access to files when you choose a file to attach (teachers, homework). The app reads only the file you pick.</li>
        </ul>
        <p>The app does not request access to the camera, microphone, contacts, calendar, SMS or call history. You can change or withdraw a permission at any time in your phone's settings; features that need it then stop working.</p>
      </section>

      <section aria-labelledby="location">
        <h2 id="location" tabindex="-1">6. Location permission: Driver live tracking</h2>
        <p>
          When a driver starts a trip or duty, the app records the vehicle's position in the background so the school can see the driver's current location, even when the screen is off or another app is in use.
        </p>
        <ul>
          <li><strong>Only during an active duty.</strong> Tracking starts when the driver starts duty and stops when the driver ends it. While it runs, the app shows a persistent notification.</li>
          <li><strong>Only drivers.</strong> Only drivers use this feature. The location of students, teachers, guardians or any other user is never collected.</li>
          <li><strong>Before you are asked.</strong> The app first shows a clear explanation and asks you to accept it. Only after that does it ask Android for the location permissions. If you choose "Not now", nothing is requested and duty cannot be started.</li>
          <li><strong>Who can see it.</strong> The School Admin and the Principal of the driver's own school can see the vehicle on the live map. Students of that school whose transport area is the one that vehicle serves can see where their bus is, and only while it is on duty. No other school and no one else can see it.</li>
          <li><strong>What is stored.</strong> Only the latest position of the vehicle is held (coordinates, accuracy, and speed and direction if the phone reports them, with the time). Each new position replaces the previous one. The position is deleted when duty starts and when duty ends. We keep no location history or trail; the duty record keeps only who drove which vehicle and when duty started and ended, without coordinates.</li>
          <li><strong>Not for advertising, never sold.</strong> Location is used only to show the school the vehicle's current position. It is not used for advertising and is not sold or shared with advertisers.</li>
        </ul>
        <p>The driver can stop tracking at any time by ending the duty in the app.</p>
      </section>

      <section aria-labelledby="sharing">
        <h2 id="sharing" tabindex="-1">7. Sharing and third parties</h2>
        <p><strong>We do not sell personal data.</strong> We disclose it only:</p>
        <ul>
          <li>to the school concerned, whose users and administrators see the information of their own school;</li>
          <li>to service providers that help us run the platform, only for that purpose; and</li>
          <li>where the law requires, for example in answer to a lawful order of a court or authority.</li>
        </ul>
        <p>The service providers and third-party services involved are:</p>
        <ul>
          <li><strong>Google Firebase Cloud Messaging</strong>, which delivers push notifications. Google receives your device's notification token and the notification text.</li>
          <li><strong>OpenStreetMap</strong>, which supplies the map pictures for the student's "Where is my bus" screen. Your device requests map tiles from the OpenStreetMap servers, which therefore receive its IP address and the map area being viewed. The bus's exact position is not sent to OpenStreetMap; it is drawn on the map inside the app.</li>
          <li><strong>Google Fonts</strong>, which supplies the website's typeface. Your browser fetches it from Google, which receives its IP address.</li>
          <li><strong>Hosting and email providers</strong>, which store the data and send the emails the platform sends, such as sign-in details to a school.</li>
        </ul>
      </section>

      <section aria-labelledby="retention">
        <h2 id="retention" tabindex="-1">8. How long we keep it</h2>
        <p>
          Records are kept only as long as needed for the purpose they were collected for, or as the school requires, and are deleted or anonymised on a valid request.
          Driver location is not kept: see section 6.
        </p>
      </section>

      <section aria-labelledby="security">
        <h2 id="security" tabindex="-1">9. How we protect it</h2>
        <ul>
          <li>The app talks to our servers only over HTTPS.</li>
          <li>Passwords are stored as one-way hashes, never as readable text. Repeated failed sign-in attempts are limited. Sign-in sessions are short-lived and are renewed automatically.</li>
          <li>In the app, sign-in tokens are kept in the device's secure storage.</li>
          <li>Each school's data is kept separate from every other school's, and people see only what their role allows.</li>
          <li>Student and staff photos, documents and attachments are not public: they are served only to signed-in users who are allowed to see them.</li>
          <li>The public forms are protected by input checks, a spam trap and a limit on how often one address can submit.</li>
        </ul>
        <p>No system is perfectly secure, and we cannot promise absolute security.</p>
      </section>

      <section aria-labelledby="your-rights">
        <h2 id="your-rights" tabindex="-1">10. Your rights and how to make a request</h2>
        <p>You can ask to see the personal information we hold about you, to have it corrected, or to have it deleted, and you can withdraw a consent you gave us.</p>
        <ul>
          <li><strong>App and platform users:</strong> please contact your school first. The school holds your records and can correct or delete them. We will help the school carry out your request.</li>
          <li><strong>Directly to us:</strong> we also handle requests sent to us. Write to <a class="pub-link" [href]="'mailto:' + site.email">{{ site.email }}</a> and tell us who you are and what you want. We may need to confirm your identity before we act.</li>
          <li><strong>Website enquiries:</strong> write to the same address and we will delete your enquiry.</li>
        </ul>
        <p>If you are not happy with our answer, tell us at the same address and we will look into it.</p>
      </section>

      <section aria-labelledby="children">
        <h2 id="children" tabindex="-1">11. Children's privacy</h2>
        <p>
          Many students who use the app are children. Their accounts are created and managed by their school, and their information is handled only through the school. We do not collect children's information through the public website, we do not show advertising in the app, and we do not track children's location. Parents and guardians who want to see, correct or delete a child's information should ask the school, or write to us at <a class="pub-link" [href]="'mailto:' + site.email">{{ site.email }}</a>.
        </p>
      </section>

      <section aria-labelledby="changes">
        <h2 id="changes" tabindex="-1">12. Changes to this policy</h2>
        <p>If we change this policy we will update the "Last updated" date at the top of this page. Please check it from time to time.</p>
      </section>

      <section aria-labelledby="contact">
        <h2 id="contact" tabindex="-1">13. Contact us</h2>
        <address>
          <strong>{{ site.legalName }}</strong><br />
          {{ site.address }}<br />
          Email: <a class="pub-link" [href]="'mailto:' + site.email">{{ site.email }}</a><br />
          Phone: <a class="pub-link" [href]="'tel:' + site.phoneHref">{{ site.phone }}</a>
        </address>
      </section>

      <p class="back">Questions? <a class="pub-link" routerLink="/contact">Contact {{ site.name }}</a>.</p>
    </article>
  `,
})
export class PrivacyPolicyComponent {
  private readonly document = inject(DOCUMENT);

  protected readonly site = SITE_CONFIG;
  protected readonly updated = PRIVACY_LAST_UPDATED;
  protected readonly toc = PRIVACY_TOC;

  /** The router scrolls to the fragment; this also moves keyboard / screen-reader focus to the heading, so the next Tab continues from there. */
  protected focusSection(id: string): void {
    this.document.getElementById(id)?.focus({ preventScroll: true });
  }
}
